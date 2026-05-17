# Skill: Run Tests

**Purpose:** Run the full AntKart test suite, report failures with file and line context, check that test count matches the expected baseline, and flag if any service has dropped tests (a sign that tests were deleted rather than fixed).

---

## When to Use
- Before committing any change
- After adding a new feature or fixing a bug
- After modifying BuildingBlocks (all services must still pass)
- As a gate before `docker-compose up --build`

---

## Test Baselines (as of last update)

| Project | Expected Tests |
|---------|---------------|
| AK.Products.Tests | 202 |
| AK.Discount.Tests | 53 |
| AK.ShoppingCart.Tests | 88 |
| AK.Order.Tests | 113 |
| AK.UserIdentity.Tests | 20 |
| AK.Payments.Tests | 69 |
| AK.Notification.Tests | 37 |
| AK.IntegrationTests | 35 |
| **Total** | **617** |

Update this table in `CLAUDE.md` whenever you add tests. If a count drops, investigate — do not ignore.

---

## Run the Full Suite

```bash
# Run all tests in the solution
dotnet test AntKart.sln --logger "console;verbosity=normal" 2>&1

# Shorter output — just pass/fail summary
dotnet test AntKart.sln --logger "console;verbosity=minimal" 2>&1

# Fastest — parallel, minimal output
dotnet test AntKart.sln -m --logger "console;verbosity=minimal" 2>&1
```

---

## Run Tests for a Single Service

```bash
dotnet test AK.Products/AK.Products.Tests/AK.Products.Tests.csproj
dotnet test AK.Discount/AK.Discount.Tests/AK.Discount.Tests.csproj
dotnet test AK.ShoppingCart/AK.ShoppingCart.Tests/AK.ShoppingCart.Tests.csproj
dotnet test AK.Order/AK.Order.Tests/AK.Order.Tests.csproj
dotnet test AK.UserIdentity/AK.UserIdentity.Tests/AK.UserIdentity.Tests.csproj
dotnet test AK.Payments/AK.Payments.Tests/AK.Payments.Tests.csproj
dotnet test AK.Notification/AK.Notification.Tests/AK.Notification.Tests.csproj
dotnet test AK.IntegrationTests/AK.IntegrationTests.csproj
```

---

## Check Test Counts Against Baseline

```bash
dotnet test AntKart.sln --logger "console;verbosity=minimal" 2>&1 \
  | grep -E "passed|failed|skipped|Total tests"
```

Expected output example:
```
Test Run Successful.
Total tests: 617
     Passed: 617
     Failed: 0
```

If `Total tests` is lower than the baseline, a test was deleted. Find it:

```bash
# Compare current test count per project
for proj in \
  AK.Products/AK.Products.Tests/AK.Products.Tests.csproj \
  AK.Discount/AK.Discount.Tests/AK.Discount.Tests.csproj \
  AK.ShoppingCart/AK.ShoppingCart.Tests/AK.ShoppingCart.Tests.csproj \
  AK.Order/AK.Order.Tests/AK.Order.Tests.csproj \
  AK.UserIdentity/AK.UserIdentity.Tests/AK.UserIdentity.Tests.csproj \
  AK.Payments/AK.Payments.Tests/AK.Payments.Tests.csproj \
  AK.Notification/AK.Notification.Tests/AK.Notification.Tests.csproj \
  AK.IntegrationTests/AK.IntegrationTests.csproj; do
  count=$(dotnet test "$proj" --logger "console;verbosity=minimal" 2>&1 | grep "Passed:" | grep -oE "[0-9]+")
  echo "$proj: $count"
done
```

---

## Diagnose a Failing Test

```bash
# Run with full verbosity to see the failing assertion and stack trace
dotnet test AK.Order/AK.Order.Tests/AK.Order.Tests.csproj \
  --logger "console;verbosity=detailed" 2>&1 \
  | grep -A 20 "FAILED\|Error\|Assert"

# Run a specific test by name
dotnet test AK.Order/AK.Order.Tests/AK.Order.Tests.csproj \
  --filter "FullyQualifiedName~CreateOrderCommandHandler"

# Run a specific test method
dotnet test AK.Order/AK.Order.Tests/AK.Order.Tests.csproj \
  --filter "Handle_ValidCommand_ReturnsOrderDto"
```

---

## Common Failure Patterns

| Failure Message | Likely Cause | Fix |
|----------------|--------------|-----|
| `NullReferenceException` in test setup | Mock not configured for a method the handler now calls | Add `mock.Setup(...)` for the new dependency |
| `FluentAssertions.Exceptions.AssertionFailedException` on DTO field | DTO field added/renamed but test expected old value | Update test expected value |
| `CS0246: type not found` | BuildingBlocks type renamed/moved | Update using statement |
| `InvalidOperationException: No constructor found` | Handler's constructor parameter added but test instantiates directly | Add new mock parameter to test constructor |
| Integration test timeout | MassTransit harness didn't receive the event in time | Increase `await harness.Consumed.Any<T>()` timeout (add overload with `TimeSpan`) |
| `DbUpdateException` in EF InMemory tests | New NOT NULL column without default — migration added a constraint EF InMemory doesn't enforce | Add default value or make column nullable in domain model |

---

## Writing New Tests — Quick Reference

All tests follow the pattern: `Method_Condition_ExpectedResult`

```csharp
public class CreateOrderCommandHandlerTests
{
    private readonly Mock<IOrderRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPublishEndpoint> _bus = new();

    [Fact]
    public async Task Handle_ValidCommand_ReturnsOrderDto()
    {
        // Arrange
        _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
        var handler = new CreateOrderCommandHandler(_repo.Object, _uow.Object, _bus.Object);
        var command = TestDataFactory.ValidCreateOrderCommand();

        // Act
        var result = await handler.Handle(command, default);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(command.UserId);
        _repo.Verify(r => r.AddAsync(It.IsAny<Order>(), default), Times.Once);
    }
}
```

**Rules:**
- Use `TestDataFactory` static class for test data builders (consistent across all tests in the service)
- Mock via interface, never via concrete class
- One `Assert` concept per test (multiple `.Should()` calls on the same object is fine; separate business rules go in separate tests)
- Never use `Thread.Sleep` — use `async/await` properly
- No network, no database, no running host in unit tests

---

## Checklist

- [ ] `dotnet build` → 0 errors before running tests
- [ ] `dotnet test` → 0 failures
- [ ] Total test count ≥ baseline (617); higher if you added tests
- [ ] No tests skipped (no `[Skip]` attribute added without justification)
- [ ] New feature has at least: handler happy path + handler failure + validator valid + validator invalid
- [ ] If a count dropped — investigated and either restored the test or updated the baseline with justification
- [ ] Baseline table in `CLAUDE.md` updated if test count changed
