# Skill: Impact Check Before a Change

**Purpose:** Before touching any shared code — a BuildingBlocks type, an integration event contract, `ocelot.json`, `docker-compose.yml`, a base entity, or an EF migration — enumerate every service, test, endpoint, consumer, and document that will be affected. Run this before writing a single line of change.

---

## When to Use
- Changing **anything in AK.BuildingBlocks** (base classes, shared middleware, extension methods, integration event contracts)
- Changing an **integration event record** (adding/removing/renaming fields)
- Changing a **shared interface** (`IRepository<T>`, `IUnitOfWork`, `IDomainEvent`)
- Changing **ocelot.json** routing, rate limits, or auth policy
- Changing **docker-compose.yml** service names, ports, or dependency chains
- Renaming or moving a shared DTO or base class
- Changing **Keycloak realm settings** that affect JWT claims

---

## Impact Check Matrix

Use this matrix to determine what to check based on what you are changing:

| What You Are Changing | Check These |
|-----------------------|-------------|
| `Entity` or `StringEntity` base class | All domain entities in all services (7 services) |
| `ValueObject` base class | `ShippingAddress` in AK.Order; any future value objects |
| `IIntegrationEvent` or event record | All publishers (grep `Publish<TEvent>`), all consumers (`IConsumer<TEvent>`), all integration tests |
| `IRepository<T>` interface | All repository interfaces and implementations (5 services) |
| `IUnitOfWork` interface | All UoW implementations, all handlers that call `SaveChangesAsync` |
| `ValidationBehavior<TRequest, TResponse>` | All services using it (all except UserIdentity) |
| `ExceptionHandlerMiddleware` (shared) | All services using it (all except UserIdentity which has its own) |
| `HttpContextExtensions.GetUserId()` | Every endpoint and handler that calls `http.GetUserId()` |
| `AuthenticationExtensions` / `KeycloakSettings` | All services calling `AddKeycloakAuthentication` |
| `MassTransitExtensions.AddRabbitMqMassTransit` | All services using MassTransit (Order, Payments, Notification, Cart, Products, UserIdentity) |
| `PagedResult<T>` or `Result<T>` | All handlers and endpoints returning these types |
| `ocelot.json` route change | Gateway, affected downstream service, ocelot.Development.json, EVENTBUS.md if routing changes |
| Integration event field added (non-breaking) | All consumers — verify they compile; new optional field with default is safe |
| Integration event field removed or renamed (breaking) | All publishers + all consumers must update simultaneously |
| docker-compose.yml service rename | `container_name`, `depends_on` references, `ASPNETCORE_ENVIRONMENT`, Gateway ocelot host entries |
| Keycloak realm change | `antkart-realm.json`, all services using JWT claims, `SECURITY_TESTS.md` |

---

## How to Run the Impact Check

### For BuildingBlocks changes

**1 — Find all usages of the type you are changing:**

```bash
# Example: changing GetUserId() signature
grep -rn "GetUserId\(\)" --include="*.cs" .

# Example: changing Entity base class
grep -rn ": Entity\b\|: StringEntity\b" --include="*.cs" .

# Example: changing IUnitOfWork
grep -rn "IUnitOfWork" --include="*.cs" .
```

**2 — For each file in the results, decide:**
- Will the change break compilation? → Must update before merging
- Will the change change runtime behaviour? → Requires a test update
- Will the change change the public API surface? → Requires Postman + design doc update

### For integration event changes

```bash
# Find all publishers
grep -rn "Publish<OrderCreatedIntegrationEvent>\|Publish(new OrderCreatedIntegrationEvent" --include="*.cs" .

# Find all consumers
grep -rn "IConsumer<OrderCreatedIntegrationEvent>" --include="*.cs" .

# Find all integration tests using this event
grep -rn "OrderCreatedIntegrationEvent" AK.IntegrationTests/ --include="*.cs"
```

For a **field rename or removal** (breaking change): all publishers and consumers must be updated in the same commit. Integration tests must be updated. Consider adding the new field first (additive), deploying, then removing the old field in a follow-up.

For a **new optional field** (non-breaking): publishers can add the field; consumers that don't need it can ignore it. JSON deserialization ignores unknown properties by default.

### For ocelot.json changes

```bash
# Confirm route doesn't conflict with existing upstream
python3 -c "
import json
with open('AK.Gateway/AK.Gateway.API/ocelot.json') as f:
    d = json.load(f)
for r in d.get('Routes', []):
    print(r.get('UpstreamPathTemplate'), '->', r.get('DownstreamHostAndPorts',[{}])[0].get('Host',''))
"

# Check ocelot.Development.json for the same route
cat AK.Gateway/AK.Gateway.API/ocelot.Development.json
```

Also update `ocelot.Development.json` with the localhost port equivalent whenever a new route is added.

### For docker-compose.yml changes

```bash
# Find all depends_on references to a service
grep -n "depends_on\|condition:" docker-compose.yml docker-compose.override.yml

# Check container_name usage inside other containers (env vars pointing to service hostname)
grep -n "ak-.*-api\|antkart-" docker-compose.yml
```

---

## Impact Report Template

Before making the change, write a brief impact report (inline comment or PR description):

```
## Impact Report: Rename GetUserId() → GetAuthenticatedUserId()

**Type:** BuildingBlocks method rename (breaking)

**Files affected:**
- AK.ShoppingCart/AK.ShoppingCart.API/Endpoints/CartEndpoints.cs (3 calls)
- AK.Order/AK.Order.API/Endpoints/OrderEndpoints.cs (4 calls)
- AK.Payments/AK.Payments.API/Endpoints/PaymentEndpoints.cs (2 calls)
- AK.Payments/AK.Payments.API/Endpoints/SavedCardEndpoints.cs (1 call)
- AK.Notification/AK.Notification.API/Endpoints/NotificationEndpoints.cs (2 calls)
- TOTAL: 12 call sites across 5 services

**Tests affected:**
- None directly (extension method is mocked via HttpContext mock)

**Docs affected:**
- CLAUDE.md — BuildingBlocks Authentication section
- All service design docs mentioning GetUserId()
- SECURITY_TESTS.md — explanation section

**Strategy:** Rename in BuildingBlocks first, then fix all call sites, build, test — single commit.
```

---

## After the Change — Verify Nothing Broke

```bash
# Full solution build
dotnet build

# Full test suite
dotnet test

# Docker rebuild of affected services
docker-compose up --build -d <service-names>

# If security-relevant change: run security checks
bash docs/skills/security-check.md   # or follow SECURITY_TESTS.md
```

---

## Checklist

- [ ] Ran `grep` to find all usages of the type/method/event being changed
- [ ] Compiled the impact list (files, tests, docs)
- [ ] Identified whether the change is breaking or non-breaking
- [ ] For breaking event contract changes: planned additive migration strategy
- [ ] Updated all affected files in one commit (no half-applied breaking changes)
- [ ] `dotnet build` → 0 errors
- [ ] `dotnet test` → all pass
- [ ] Ran [sync-docs.md](sync-docs.md) checklist
