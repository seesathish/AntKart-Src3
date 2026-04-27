# AK.BuildingBlocks — Technical Reference

`AK.BuildingBlocks` is the single shared library consumed by every microservice in AntKart. It holds the cross-cutting contracts, base classes, and infrastructure helpers that would otherwise be copy-pasted across services. No business logic lives here — only mechanics.

---

## Contents

| Namespace | What's inside |
|-----------|--------------|
| `AK.BuildingBlocks.DDD` | Entity, StringEntity, ValueObject, IDomainEvent, IAggregateRoot |
| `AK.BuildingBlocks.Common` | PagedResult\<T\>, Result\<T\> |
| `AK.BuildingBlocks.Exceptions` | NotFoundException, ValidationException |
| `AK.BuildingBlocks.Authentication` | AuthenticationExtensions, HttpContextExtensions, KeycloakSettings |
| `AK.BuildingBlocks.Behaviors` | ValidationBehavior\<TRequest, TResponse\> |
| `AK.BuildingBlocks.Middleware` | ExceptionHandlerMiddleware, CorrelationIdMiddleware |
| `AK.BuildingBlocks.Messaging` | IIntegrationEvent, MassTransitExtensions, all integration event records |
| `AK.BuildingBlocks.Resilience` | ResilienceExtensions (HTTP, Redis, Npgsql) |
| `AK.BuildingBlocks.Logging` | SerilogExtensions |
| `AK.BuildingBlocks.HealthChecks` | HealthCheckExtensions |
| `AK.BuildingBlocks.Swagger` | SwaggerExtensions |
| `AK.BuildingBlocks.Versioning` | ApiVersioningExtensions |

---

## DDD — Domain Model Contracts

### `Entity` — Guid-keyed aggregate base

```csharp
// Used by: AK.Order, AK.Payments, AK.Notification
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }   // null until first mutation

    protected void AddDomainEvent(IDomainEvent e) { ... }
    public    void ClearDomainEvents()             { ... }     // called by UoW after dispatch
    protected void SetUpdatedAt()                  { ... }     // call inside mutating methods
}
```

**How services use it**

```csharp
// AK.Order.Domain/Entities/Order.cs
public sealed class Order : Entity, IAggregateRoot
{
    public void UpdateStatus(OrderStatus newStatus)
    {
        // ... business rule check ...
        Status = newStatus;
        SetUpdatedAt();                         // stamps UpdatedAt, inherited from Entity
        AddDomainEvent(new OrderStatusChangedEvent(Id, newStatus));
    }
}
```

**Domain event lifecycle**

```
Entity.AddDomainEvent()  →  Unit of Work dispatches AFTER SaveChangesAsync()  →  Entity.ClearDomainEvents()
```

Events are dispatched only for changes that actually persisted. The UoW calls `ClearDomainEvents()` after dispatch so they don't replay on the next save cycle.

---

### `StringEntity` — String-keyed aggregate base (MongoDB)

```csharp
// Used by: AK.Products
public abstract class StringEntity
{
    public string Id { get; protected set; } = Guid.NewGuid().ToString("N");  // 32-char hex
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }

    protected void AddDomainEvent(IDomainEvent e) { ... }
    public    void ClearDomainEvents()             { ... }
    protected void SetUpdatedAt()                  { ... }
}
```

`ToString("N")` produces a compact 32-character hex string (e.g. `a1b2c3d4e5f6...`). MongoDB stores it as a BSON string, not an ObjectId. The `BsonClassMap` in `AK.Products.Infrastructure` registers this mapping — no Bson attributes appear on the domain entity.

---

### `ValueObject` — Structural equality base

Value objects have no identity; two instances are equal when all their fields are equal. Override `GetEqualityComponents()` and equality mechanics (`Equals`, `GetHashCode`, `==`, `!=`) are derived automatically.

```csharp
// AK.BuildingBlocks.DDD
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj) { ... }      // SequenceEqual on components
    public override int GetHashCode()        { ... }      // Aggregate + HashCode.Combine
    public static bool operator ==(ValueObject? l, ValueObject? r) => ...
    public static bool operator !=(ValueObject? l, ValueObject? r) => ...
}
```

**How services use it**

```csharp
// AK.Order.Domain/ValueObjects/ShippingAddress.cs
public sealed class ShippingAddress : ValueObject
{
    public string FullName    { get; private set; }
    public string AddressLine1 { get; private set; }
    // ... 6 more fields ...

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FullName;
        yield return AddressLine1;
        yield return AddressLine2;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
        yield return Phone;
    }
}
```

All 8 fields participate in equality — two shipping addresses are the same only when every field matches.

> **Note on alternatives:** `Money` in `AK.Products` uses a C# `record` instead of extending `ValueObject`. Both work. Records give you equality for free via compiler synthesis; `ValueObject` makes the mechanics explicit and works on classes that cannot use record syntax (e.g. EF Core owned types).

---

### `IDomainEvent` — Domain event marker

```csharp
public interface IDomainEvent { }
```

Implement on any `record` that represents "something meaningful happened" inside an entity.

```csharp
// AK.Order.Domain/Events/
public record OrderCreatedEvent(Guid OrderId, string OrderNumber) : IDomainEvent;
public record OrderStatusChangedEvent(Guid OrderId, OrderStatus NewStatus) : IDomainEvent;
public record OrderCancelledEvent(Guid OrderId) : IDomainEvent;
```

---

### `IAggregateRoot` — Aggregate boundary marker

```csharp
public interface IAggregateRoot { }
```

A marker interface. External code may only hold references to aggregate roots; internal child entities are accessed only through the root. Repositories return only aggregate roots.

```csharp
public sealed class Order : Entity, IAggregateRoot { ... }       // ✅ aggregate root
public sealed class OrderItem : Entity { ... }                    // ✅ internal entity
public interface IOrderRepository : IRepository<Order> { ... }   // ✅ works with root only
```

---

## Common — Shared primitives

### `PagedResult<T>`

```csharp
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int  TotalPages      => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage     => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

All paginated query handlers return `PagedResult<TDto>`. Clients receive `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasNextPage`, `hasPreviousPage`.

### `Result<T>`

```csharp
public class Result<T>
{
    public bool   IsSuccess { get; }
    public T?     Value     { get; }
    public string? Error    { get; }

    public static Result<T> Success(T value)   => new(value);
    public static Result<T> Failure(string err) => new(err);
}
```

---

## Authentication — Keycloak JWT helpers

### `AuthenticationExtensions`

Single call wires JWT Bearer auth against Keycloak for an entire service.

```csharp
// In any service Program.cs:
builder.Services.AddKeycloakAuthentication(builder.Configuration);
// ...
app.UseKeycloakAuth();   // UseAuthentication() + UseAuthorization() in correct order
```

Internally it:
- Downloads Keycloak signing keys automatically from the OIDC discovery endpoint
- Validates the `azp` claim to reject tokens issued for a different client (cross-client token reuse prevention)
- Parses the `realm_access.roles` JSON claim and adds each role as a `ClaimTypes.Role` so `RequireRole("admin")` works

**Configuration** (`appsettings.json`):

```json
{
  "Keycloak": {
    "Authority": "http://keycloak:8090/realms/antkart",
    "Audience": "antkart-client",
    "RequireHttpsMetadata": false
  }
}
```

Named policies registered automatically:

| Policy name | Rule |
|-------------|------|
| `"admin"` | `RequireRole("admin")` |
| `"authenticated"` | `RequireAuthenticatedUser()` |

Usage in endpoints:

```csharp
group.MapGet("/admin/report", handler).RequireAuthorization("admin");
group.MapGet("/me", handler).RequireAuthorization("authenticated");
```

---

### `HttpContextExtensions` — User identity from JWT

```csharp
// Security rule: always derive userId from the JWT, never from URL params or request body.
// Prevents IDOR — a logged-in user cannot access another user's data by changing a URL parameter.

string userId   = http.GetUserId();          // 'sub' claim (Keycloak UUID); throws 403 if absent
string email    = http.GetUserEmail();       // 'email' claim; returns "" if absent
string name     = http.GetUserDisplayName(); // 'name' / 'given_name'+'family_name' / 'preferred_username'
```

`GetUserId()` throws `UnauthorizedAccessException` if no identity claim is present, which `ExceptionHandlerMiddleware` maps to HTTP 403.

**Services that use it:** ShoppingCart, Order, Payments, SavedCards

---

## Behaviors — MediatR pipeline

### `ValidationBehavior<TRequest, TResponse>`

```csharp
// Wired in every service's AddApplication():
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

Pipeline order for every `mediator.Send()` call:

```
ValidationBehavior  →  actual command/query handler
```

If any registered `IValidator<TRequest>` finds errors, `ValidationException` is thrown and the handler is never called. `ExceptionHandlerMiddleware` maps `ValidationException` → HTTP 400.

```csharp
// Example — Products service AddApplication() in ServiceCollectionExtensions.cs:
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly));
services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

No per-service copy needed; the behavior lives in BuildingBlocks and is type-generic.

---

## Middleware

### `ExceptionHandlerMiddleware`

Maps domain exceptions to HTTP status codes. Every REST service (except UserIdentity) adds this at the top of the pipeline.

```csharp
app.UseMiddleware<ExceptionHandlerMiddleware>();
```

| Exception | HTTP Status | Scenario |
|-----------|-------------|----------|
| `FluentValidation.ValidationException` | 400 Bad Request | Invalid input |
| `UnauthorizedAccessException` | 403 Forbidden | IDOR / ownership check |
| `KeyNotFoundException` | 404 Not Found | Resource missing |
| `InvalidOperationException` | 409 Conflict | Business rule violation |
| `Exception` (catch-all) | 500 Internal Server Error | Unhandled |

> **Exception:** `AK.UserIdentity` keeps its own middleware because it maps `UnauthorizedAccessException` → 401 (not 403) and has no FluentValidation.

---

### `CorrelationIdMiddleware`

Reads or generates an `X-Correlation-Id` header and sets it on every response. All Serilog log entries include the correlation ID, enabling cross-service request tracing in Kibana.

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();
```

---

## Messaging — Event bus contracts

### `IIntegrationEvent`

Marker interface for messages published to RabbitMQ. All integration event records implement this.

### Integration events

All integration events live in `AK.BuildingBlocks.Messaging.IntegrationEvents` so every service shares the same contract without needing a project reference to the publisher.

| Event | Published by | Consumed by |
|-------|-------------|-------------|
| `OrderCreatedIntegrationEvent` | AK.Order | Notification, Order SAGA |
| `OrderConfirmedIntegrationEvent` | Order SAGA | Order, ShoppingCart, Notification |
| `OrderCancelledIntegrationEvent` | AK.Order | Notification |
| `StockReservedIntegrationEvent` | AK.Products | Order SAGA |
| `StockReservationFailedIntegrationEvent` | AK.Products | Order SAGA |
| `PaymentInitiatedIntegrationEvent` | AK.Payments | (audit / integration tests) |
| `PaymentSucceededIntegrationEvent` | AK.Payments | AK.Order, Notification |
| `PaymentFailedIntegrationEvent` | AK.Payments | AK.Order, Notification |
| `UserRegisteredIntegrationEvent` | AK.UserIdentity | Notification |

### `MassTransitExtensions.AddRabbitMqMassTransit()`

```csharp
// In each service's Infrastructure ServiceCollectionExtensions.cs:
services.AddRabbitMqMassTransit(configuration, "order", x =>
{
    x.AddConsumer<PaymentSucceededConsumer>();
    x.AddSagaStateMachine<OrderSaga, OrderSagaState>()
        .EntityFrameworkRepository(r => { ... });
});
```

The `servicePrefix` ("order", "notification", "payments", "cart", "products", "identity") ensures every service gets its own uniquely-named RabbitMQ queue per consumer. Without the prefix, two services consuming the same event would compete (only one receives each message). With prefixes:

```
"order-payment-failed"        ← AK.Order queue
"notification-payment-failed" ← AK.Notification queue
```

Both queues bind to the same exchange — fan-out, not competing consumers.

Global retry policy (built-in): 3 attempts with incremental 1s/3s/5s delays before moving to the error queue.

---

## Resilience — Polly v8 pipelines

### HTTP resilience

```csharp
// Attach retry + circuit breaker + timeout to any HttpClient:
builder.Services.AddHttpClient<IKeycloakService, KeycloakService>()
    .AddHttpResilienceWithCircuitBreaker();
```

Pipeline layers (outermost → innermost):

```
Retry (exponential backoff + jitter, 3 attempts)
  → Circuit Breaker (>50% failure rate over 60s → 30s break)
    → Timeout (15s per attempt)
```

### Redis resilience

```csharp
services.AddRedisResilience();  // 3 retries, exponential backoff, 5s timeout
```

Use the named pipeline in your Redis repository:

```csharp
await _pipeline.ExecuteAsync(async ct => await _db.StringSetAsync(key, value), ct);
```

### PostgreSQL (Npgsql) resilience

```csharp
services.AddNpgsqlResilience();  // 3 retries, exponential backoff, 30s timeout
```

Exponential backoff + jitter prevents all services from hammering the database simultaneously after it recovers from a brief outage (thundering herd prevention).

---

## Logging — Serilog

```csharp
// Program.cs:
builder.AddSerilogLogging();
```

Configures Serilog with:
- **Console sink** — structured JSON in Docker, human-readable in development
- **Rolling file sink** — `logs/log-.txt` with daily rotation and 7-day retention
- **Elasticsearch sink** — ships to `http://elasticsearch:9200`; Kibana reads from here

All log entries include the `X-Correlation-Id` enricher added by `CorrelationIdMiddleware`.

---

## Health Checks

```csharp
// Program.cs (registration + mapping):
builder.Services.AddDefaultHealthChecks();
// ...
app.MapDefaultHealthChecks();
```

Exposes `GET /health` with a self-check (`HealthCheckResult.Healthy()`). Services add their own infrastructure checks (Redis, MongoDB, PostgreSQL) on top:

```csharp
// AK.ShoppingCart adds:
services.AddHealthChecks()
    .AddRedis(redisConnection);
```

---

## Swagger

### `SwaggerExtensions.UseSwaggerInDevelopment()`

```csharp
// Program.cs in every REST service:
app.UseSwaggerInDevelopment("AK.Order API v1");
```

Gates `UseSwagger()` + `UseSwaggerUI()` to the `Development` environment only. `AddSwaggerGen()` (the DI registration) remains in each service's `Program.cs` — only the middleware that serves the JSON spec and the Swagger UI is suppressed in non-Development environments (Docker runs as `Production`).

---

## Versioning

### `ApiVersioningExtensions.AddStandardApiVersioning()`

```csharp
// Program.cs (API versioning):
builder.Services.AddStandardApiVersioning();
```

Registers `Asp.Versioning.Http` with these defaults:

| Setting | Value |
|---------|-------|
| Default version | `1.0` |
| Assume default when unspecified | `true` — existing clients without a version header continue to work |
| Report versions | `true` — adds `api-supported-versions` response header |
| Version readers | URL segment (`/api/v1/`) **and** `api-version` header (both accepted) |

**Adding v2 to an endpoint group:**

```csharp
var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .Build();

var v1 = app.MapGroup("/api/v1/orders").WithApiVersionSet(versionSet).MapToApiVersion(1, 0);
var v2 = app.MapGroup("/api/v2/orders").WithApiVersionSet(versionSet).MapToApiVersion(2, 0);
```

v1 and v2 coexist — existing clients are unaffected when v2 launches. Currently demonstrated in `AK.Order`; other services adopt it by calling `AddStandardApiVersioning()` in their `Program.cs`.

---

## Adding a new service — BuildingBlocks checklist

When building a new microservice, use these BuildingBlocks components:

```
1. Domain layer (.csproj)
   ProjectReference → AK.BuildingBlocks (for DDD types)
   Entity or StringEntity  ← pick based on DB (PostgreSQL vs MongoDB)
   IAggregateRoot          ← mark aggregate roots
   IDomainEvent            ← domain event records

2. Application layer (.csproj)
   ProjectReference → AK.BuildingBlocks
   ValidationBehavior<,>   ← wire in AddApplication()
   PagedResult<TDto>       ← return from paginated queries
   Result<TDto>            ← optional envelope for failures

3. API / Grpc layer
   AddKeycloakAuthentication()     ← JWT auth
   UseKeycloakAuth()               ← auth middleware
   AddStandardApiVersioning()      ← URL-segment + header versioning
   UseMiddleware<ExceptionHandlerMiddleware>()
   UseMiddleware<CorrelationIdMiddleware>()
   UseSwaggerInDevelopment("AK.<Name> API v1")
   AddDefaultHealthChecks() / MapDefaultHealthChecks()
   AddSerilogLogging()

4. Infrastructure layer
   AddRabbitMqMassTransit(config, "<prefix>", x => { ... })
   AddHttpResilienceWithCircuitBreaker()   ← for outbound HTTP clients
   AddRedisResilience()                    ← if using Redis
   AddNpgsqlResilience()                   ← if using PostgreSQL

5. User-scoped endpoints
   var userId = http.GetUserId();          ← NEVER accept userId from URL or body
```

---

## Dependency graph

```
AK.Products.Domain         →  AK.BuildingBlocks (StringEntity, IDomainEvent, IAggregateRoot)
AK.Order.Domain            →  AK.BuildingBlocks (Entity, IDomainEvent, IAggregateRoot, ValueObject)
AK.Payments.Domain         →  AK.BuildingBlocks (Entity, IDomainEvent, IAggregateRoot)
AK.Notification.Domain     →  AK.BuildingBlocks (Entity, IDomainEvent, IAggregateRoot)

AK.*.Application           →  AK.BuildingBlocks (ValidationBehavior, PagedResult, Result, IIntegrationEvent)
AK.*.Infrastructure        →  AK.BuildingBlocks (MassTransitExtensions, ResilienceExtensions, IntegrationEvents)
AK.*.API / .Grpc           →  AK.BuildingBlocks (Auth, Middleware, Swagger, HealthChecks, Logging)
```

`AK.Discount` (gRPC) is the exception — it uses its own lighter setup with no MassTransit or Keycloak JWT (it is called service-to-service by AK.Products, not by end users).
