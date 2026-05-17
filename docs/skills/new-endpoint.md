# Skill: Add a New Endpoint

**Purpose:** Add one Minimal API endpoint to an existing AntKart service following the full CQRS stack: command or query record → FluentValidation validator → MediatR handler → DTO → mapper → endpoint registration → unit tests. Includes IDOR-safe userId wiring and correct auth policy.

---

## When to Use
- Adding a new operation to an existing service (e.g. `GET /api/orders/{id}/timeline`, `POST /api/payments/refund`)
- The operation is a first-class business action, not a minor tweak to an existing handler

## Prerequisites
- The service builds and all its existing tests pass
- You know whether this is a **Command** (mutates state, returns DTO or void) or a **Query** (reads state, returns DTO or `PagedResult<T>`)

---

## Step 1 — Define the Request and Response DTOs

DTOs are `record` types in `AK.<Service>.Application/DTOs/` (or inside the feature folder for Vertical Slice services like Order):

```csharp
// Command input — no userId field; injected from JWT in the endpoint
public record CreateRefundRequest(Guid PaymentId, decimal Amount, string Reason);

// Response DTO — never return the domain entity
public record RefundDto(Guid Id, Guid PaymentId, decimal Amount, string Status, DateTimeOffset CreatedAt);
```

**Rules:**
- Never include `userId` in a request DTO — always derive from JWT
- Never expose domain entity types — always map to a DTO
- Use `record` not `class`

---

## Step 2 — Define the MediatR Command or Query

Place in `AK.<Service>.Application/Features/<FeatureName>/` (Vertical Slice) or `AK.<Service>.Application/Commands/` or `AK.<Service>.Application/Queries/`:

```csharp
// Command
public record CreateRefundCommand(
    Guid UserId,
    Guid PaymentId,
    decimal Amount,
    string Reason) : IRequest<RefundDto>;

// Query
public record GetUserRefundsQuery(
    Guid UserId,
    int Page,
    int PageSize) : IRequest<PagedResult<RefundDto>>;
```

---

## Step 3 — Write the FluentValidation Validator

Place in `AK.<Service>.Application/Validators/` (or feature folder):

```csharp
public class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Refund amount must be greater than zero.");
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}
```

`ValidationBehavior<TRequest, TResponse>` in the MediatR pipeline automatically runs this before the handler. The `ExceptionHandlerMiddleware` maps `ValidationException` → HTTP 400.

---

## Step 4 — Implement the Handler

```csharp
internal sealed class CreateRefundCommandHandler(
    IRefundRepository refundRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateRefundCommand, RefundDto>
{
    public async Task<RefundDto> Handle(CreateRefundCommand request, CancellationToken ct)
    {
        // Domain logic — never call DbContext directly; go through repository
        var refund = Refund.Create(request.PaymentId, request.UserId, request.Amount, request.Reason);
        await refundRepository.AddAsync(refund, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return refund.ToDto();
    }
}
```

**Rules:**
- `internal sealed` — handlers are not part of the public API
- Use constructor injection (primary constructor syntax)
- Never return the domain entity — call `.ToDto()` mapper
- For expected failures (not found, invalid state), throw `KeyNotFoundException` or `InvalidOperationException` — the middleware maps them to 404/409

---

## Step 5 — Add the Mapper

Create or extend `AK.<Service>.Application/Mappers/<Entity>Mapper.cs`:

```csharp
internal static class RefundMapper
{
    internal static RefundDto ToDto(this Refund refund) =>
        new(refund.Id, refund.PaymentId, refund.Amount, refund.Status.ToString(), refund.CreatedAt);
}
```

---

## Step 6 — Register the Endpoint

In the service's endpoint class (e.g. `AK.<Service>.API/Endpoints/<Entity>Endpoints.cs`), add inside `Map<Entity>Endpoints()`:

```csharp
// POST /api/payments/refunds
group.MapPost("/refunds", async (
    HttpContext http,
    CreateRefundRequest req,
    IMediator mediator) =>
{
    var userId = http.GetUserId();      // throws UnauthorizedAccessException → 403 if no JWT sub
    var result = await mediator.Send(new CreateRefundCommand(userId, req.PaymentId, req.Amount, req.Reason));
    return Results.Created($"/api/payments/refunds/{result.Id}", result);
})
.WithName("CreateRefund");
```

**Auth policy on the group:**
```csharp
var group = app.MapGroup("/api/payments")
    .WithTags("Payments")
    .RequireAuthorization("authenticated");   // all endpoints need valid JWT
```

For admin-only endpoints, add `.RequireAuthorization("admin")` on the individual `MapGet/MapPost/MapPut/MapDelete` call, **not** on the group.

---

## Step 7 — IDOR Ownership Check (for single-resource GET/DELETE)

When returning or mutating a resource identified by an ID, verify the caller owns it:

```csharp
group.MapGet("/refunds/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
{
    var userId = http.GetUserId();
    var refund = await mediator.Send(new GetRefundByIdQuery(id, userId));
    return refund is null ? Results.NotFound() : Results.Ok(refund);
})
.WithName("GetRefundById");
```

In the handler, throw `UnauthorizedAccessException` if `refund.UserId != request.UserId` (and the caller is not admin). The middleware maps this → 403. See [verify-idor.md](verify-idor.md) for the full pattern.

---

## Step 8 — Add Gateway Route

If this endpoint needs to be accessible through the Gateway, add a route to `ocelot.json`. See [add-gateway-route.md](add-gateway-route.md).

---

## Step 9 — Write Unit Tests

In `AK.<Service>.Tests/`, create or extend test files. Name: `<Handler>Tests.cs`. Naming convention: `Method_Condition_ExpectedResult`.

**Handler test (happy path + failure):**
```csharp
public class CreateRefundCommandHandlerTests
{
    private readonly Mock<IRefundRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    [Fact]
    public async Task Handle_ValidCommand_ReturnsRefundDto()
    {
        // Arrange
        var command = new CreateRefundCommand(Guid.NewGuid(), Guid.NewGuid(), 50m, "Duplicate charge");
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        var handler = new CreateRefundCommandHandler(_repo.Object, _uow.Object);

        // Act
        var result = await handler.Handle(command, default);

        // Assert
        result.Amount.Should().Be(50m);
        _repo.Verify(r => r.AddAsync(It.IsAny<Refund>(), default), Times.Once);
    }
}
```

**Validator test:**
```csharp
public class CreateRefundCommandValidatorTests
{
    private readonly CreateRefundCommandValidator _validator = new();

    [Fact]
    public void Validate_NegativeAmount_ReturnsError()
    {
        var cmd = new CreateRefundCommand(Guid.NewGuid(), Guid.NewGuid(), -10m, "test");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        var cmd = new CreateRefundCommand(Guid.NewGuid(), Guid.NewGuid(), 50m, "Duplicate charge");
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
```

Run: `dotnet test AK.<Service>/AK.<Service>.Tests/AK.<Service>.Tests.csproj`

---

## Step 10 — Update Documentation

Run [sync-docs.md](sync-docs.md) checklist after adding the endpoint. At minimum:
- Add the new endpoint to the **API Endpoints** table in `<SERVICE>_TECHNICAL_DESIGN.md`
- Add the new request to `AntKart.postman_collection.json` under the service folder
- If this is a new pattern (e.g. first use of `Result<T>` in this service), note it in `CLAUDE.md`

---

## Checklist

- [ ] DTO record defined (no `userId` field)
- [ ] Command/Query record defined
- [ ] Validator written (all business rules covered)
- [ ] Handler implemented (`internal sealed`, mapper used, no direct DbContext)
- [ ] Mapper method added
- [ ] Endpoint registered with correct HTTP method and route
- [ ] `http.GetUserId()` used — no `userId` in route path or request body
- [ ] Auth policy correct (`authenticated` group, `admin` on specific routes only)
- [ ] Ownership check for single-resource reads/deletes
- [ ] Unit tests: handler happy path + handler failure + validator valid + validator invalid
- [ ] `dotnet build` → 0 errors
- [ ] `dotnet test` → all pass, test count increased
- [ ] Design doc endpoint table updated
- [ ] Postman request added
