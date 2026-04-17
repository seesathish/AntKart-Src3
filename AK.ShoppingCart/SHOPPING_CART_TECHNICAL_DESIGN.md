# AK.ShoppingCart Microservice — Technical Design Document

## Table of Contents
1. [Overview](#1-overview)
2. [Functional Requirements](#2-functional-requirements)
3. [Non-Functional Requirements](#3-non-functional-requirements)
4. [High-Level Architecture](#4-high-level-architecture)
5. [Solution Structure](#5-solution-structure)
6. [Domain Layer Design](#6-domain-layer-design)
7. [Application Layer Design](#7-application-layer-design)
8. [Infrastructure Layer Design](#8-infrastructure-layer-design)
9. [API Layer Design](#9-api-layer-design)
10. [Data Model](#10-data-model)
11. [CQRS & MediatR Pipeline](#11-cqrs--mediatr-pipeline)
12. [Redis Storage Strategy](#12-redis-storage-strategy)
13. [Unit of Work Pattern](#13-unit-of-work-pattern)
14. [API Reference](#14-api-reference)
15. [Testing Strategy](#15-testing-strategy)
16. [Configuration & Deployment](#16-configuration--deployment)
17. [Design Decisions & Trade-offs](#17-design-decisions--trade-offs)

---

## 1. Overview

**AK.ShoppingCart** is a .NET 9 microservice responsible for managing shopping carts within the AntKart e-commerce platform. It provides a full cart lifecycle: create, add items, update quantities, remove items, retrieve, and clear the cart. All cart state is stored in **Redis** for sub-millisecond reads and writes with automatic TTL-based expiry.

| Attribute       | Value                                  |
|-----------------|----------------------------------------|
| Framework       | .NET 9 (ASP.NET Core Minimal API)      |
| Architecture    | DDD + Clean Architecture               |
| Database        | Redis (StackExchange.Redis)            |
| Pattern Stack   | CQRS, MediatR, FluentValidation, Unit of Work |
| Namespace root  | `AK.ShoppingCart`                      |

---

## 2. Functional Requirements

### 2.1 Cart Management

| ID     | Requirement |
|--------|-------------|
| FR-01  | Retrieve the current cart for a given user; return null / 404 if no cart exists |
| FR-02  | Add a product to the cart; if the cart does not exist, create it first |
| FR-03  | If the same product is added again, increment its quantity rather than creating a duplicate line |
| FR-04  | Update the quantity of an existing cart item; quantity of 0 removes the item |
| FR-05  | Remove a specific item from the cart by product ID |
| FR-06  | Clear all items from the cart in a single operation |
| FR-07  | Return the updated cart state after every mutating operation |
| FR-08  | Report 404 when operating on a cart or item that does not exist |

### 2.2 Cart Item Attributes

| Attribute    | Type      | Notes                          |
|--------------|-----------|--------------------------------|
| ProductId    | string    | Required, unique within cart   |
| ProductName  | string    | Required, max 200 chars        |
| SKU          | string    | Required, max 50 chars         |
| Price        | decimal   | Required, must be > 0          |
| Quantity     | int       | Required, must be > 0 on add; ≥ 0 on update (0 = remove) |
| ImageUrl     | string?   | Optional product image URL     |
| SubTotal     | decimal   | Computed: Price × Quantity     |

### 2.3 Cart Aggregate Attributes

| Attribute    | Type                       | Notes                          |
|--------------|----------------------------|--------------------------------|
| UserId       | string                     | Partition key; max 100 chars   |
| Items        | IReadOnlyList\<CartItem\>  | Ordered line items             |
| TotalAmount  | decimal                    | Computed: sum of all SubTotals |
| TotalItems   | int                        | Computed: sum of all Quantities|
| CreatedAt    | DateTime (UTC)             | Set on cart creation           |
| UpdatedAt    | DateTime (UTC)             | Updated on every mutation      |

### 2.4 Business Rules

- A cart is created implicitly on the first `AddToCart` call — no explicit "create cart" endpoint.
- Adding an item whose `ProductId` already exists in the cart merges quantities rather than duplicating.
- `UpdateItemQuantity` with `Quantity = 0` is semantically equivalent to `RemoveItem`.
- `UpdateItemQuantity` and `RemoveItem` throw `KeyNotFoundException` when the cart or item is absent — propagated as HTTP 404.
- Cart data auto-expires after 30 days (configurable) via Redis TTL; expiry is reset on every write.
- Domain events (`CartItemAddedEvent`, `CartItemRemovedEvent`, `CartClearedEvent`) are raised in-memory for future pub/sub integration.

---

## 3. Non-Functional Requirements

| NFR    | Requirement |
|--------|-------------|
| NFR-01 | All endpoints respond within 50ms under normal load (Redis is in-memory) |
| NFR-02 | Horizontal scalability — stateless API, all state in Redis |
| NFR-03 | Input validation on all write endpoints (400 on failure) |
| NFR-04 | Structured JSON error responses for 400 / 404 / 409 / 500 |
| NFR-05 | Full OpenAPI/Swagger documentation served at `/swagger` |
| NFR-06 | Cart keys namespaced under `AKCart:cart:{userId}` to prevent collisions |
| NFR-07 | Cart TTL reset on every write to implement a sliding 30-day window |
| NFR-08 | Unit tests for all public domain, application, and infrastructure methods |

---

## 4. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      Client / API Gateway                       │
└───────────────────────────────┬─────────────────────────────────┘
                                │ HTTP/REST
┌───────────────────────────────▼─────────────────────────────────┐
│                 AK.ShoppingCart.API (Minimal API)               │
│  ┌─────────────┐  ┌──────────────────┐  ┌──────────────────┐   │
│  │  Endpoints  │  │ ExceptionHandler  │  │  Swagger/OpenAPI │   │
│  │  (5 routes) │  │   Middleware      │  │                  │   │
│  └──────┬──────┘  └──────────────────┘  └──────────────────┘   │
└─────────┼───────────────────────────────────────────────────────┘
          │ IMediator.Send()
┌─────────▼───────────────────────────────────────────────────────┐
│                AK.ShoppingCart.Application                      │
│  ┌──────────────────────────┐  ┌─────────────────────────────┐  │
│  │   Commands (CQRS Write)  │  │   Queries (CQRS Read)       │  │
│  │   AddToCart              │  │   GetCart                   │  │
│  │   RemoveFromCart         │  └─────────────────────────────┘  │
│  │   UpdateCartItem         │  ┌─────────────────────────────┐  │
│  │   ClearCart              │  │   ValidationBehavior        │  │
│  └──────────────────────────┘  │   (MediatR Pipeline)        │  │
│  ┌──────────────────────────┐  └─────────────────────────────┘  │
│  │   FluentValidation       │                                    │
│  │   Validators             │                                    │
│  └──────────────────────────┘                                   │
└─────────┼───────────────────────────────────────────────────────┘
          │ IUnitOfWork / ICartRepository
┌─────────▼───────────────────────────────────────────────────────┐
│               AK.ShoppingCart.Infrastructure                    │
│  ┌──────────────────────────┐  ┌─────────────────────────────┐  │
│  │   RedisContext           │  │   CartRepository            │  │
│  │   (IConnectionMultiplexer│  │   (JSON snapshot pattern)   │  │
│  │    wrapping)             │  └─────────────────────────────┘  │
│  └──────────────────────────┘  ┌─────────────────────────────┐  │
│  ┌──────────────────────────┐  │   UnitOfWork                │  │
│  │   RedisSettings          │  │   (lazy repo, no-op save)   │  │
│  │                          │  └─────────────────────────────┘  │
│  └──────────────────────────┘                                   │
└─────────┼───────────────────────────────────────────────────────┘
          │ StackExchange.Redis
┌─────────▼───────────────────────────────────────────────────────┐
│                           Redis                                 │
│   Key: AKCart:cart:{userId}   TTL: 30 days (sliding)           │
└─────────────────────────────────────────────────────────────────┘
```

### Layer Dependencies

```
API → Application → Domain
Infrastructure → Application → Domain
Tests → Application, Domain, Infrastructure
```

> Infrastructure depends on Application (through interfaces), never the reverse — Dependency Inversion Principle.

---

## 5. Solution Structure

```
AK.ShoppingCart/
├── AK.ShoppingCart.Domain/
│   ├── AK.ShoppingCart.Domain.csproj
│   ├── Entities/
│   │   ├── Cart.cs                   # Aggregate root
│   │   └── CartItem.cs               # Line item entity
│   └── Events/
│       ├── CartItemAddedEvent.cs
│       ├── CartItemRemovedEvent.cs
│       └── CartClearedEvent.cs
│
├── AK.ShoppingCart.Application/
│   ├── AK.ShoppingCart.Application.csproj
│   ├── Behaviors/
│   │   └── ValidationBehavior.cs     # MediatR pipeline validation
│   ├── Commands/
│   │   ├── AddToCart/
│   │   │   ├── AddToCartCommand.cs
│   │   │   └── AddToCartCommandHandler.cs
│   │   ├── RemoveFromCart/
│   │   │   ├── RemoveFromCartCommand.cs
│   │   │   └── RemoveFromCartCommandHandler.cs
│   │   ├── UpdateCartItem/
│   │   │   ├── UpdateCartItemCommand.cs
│   │   │   └── UpdateCartItemCommandHandler.cs
│   │   └── ClearCart/
│   │       ├── ClearCartCommand.cs
│   │       └── ClearCartCommandHandler.cs
│   ├── Common/
│   │   └── CartMapper.cs             # Domain → DTO
│   ├── DTOs/
│   │   ├── AddCartItemDto.cs
│   │   ├── UpdateCartItemDto.cs
│   │   ├── CartItemDto.cs
│   │   └── CartDto.cs
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs
│   ├── Interfaces/
│   │   ├── ICartRepository.cs
│   │   └── IUnitOfWork.cs
│   ├── Queries/
│   │   └── GetCart/
│   │       ├── GetCartQuery.cs
│   │       └── GetCartQueryHandler.cs
│   └── Validators/
│       ├── AddToCartValidator.cs
│       └── UpdateCartItemValidator.cs
│
├── AK.ShoppingCart.Infrastructure/
│   ├── AK.ShoppingCart.Infrastructure.csproj
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs
│   └── Persistence/
│       ├── RedisContext.cs
│       ├── RedisSettings.cs
│       ├── UnitOfWork.cs
│       └── Repositories/
│           └── CartRepository.cs
│
├── AK.ShoppingCart.API/
│   ├── AK.ShoppingCart.API.csproj
│   ├── Dockerfile
│   ├── Endpoints/
│   │   └── CartEndpoints.cs          # All 5 routes
│   ├── Middleware/
│   │   └── ExceptionHandlerMiddleware.cs
│   ├── Program.cs
│   ├── appsettings.json
│   └── appsettings.Development.json
│
└── AK.ShoppingCart.Tests/
    ├── AK.ShoppingCart.Tests.csproj
    ├── Common/
    │   └── TestDataFactory.cs
    ├── Domain/
    │   ├── CartTests.cs              # 16 tests
    │   └── CartItemTests.cs          # 9 tests
    ├── Application/
    │   ├── Commands/
    │   │   ├── AddToCartCommandHandlerTests.cs    # 3 tests
    │   │   ├── RemoveFromCartCommandHandlerTests.cs # 3 tests
    │   │   ├── UpdateCartItemCommandHandlerTests.cs # 3 tests
    │   │   └── ClearCartCommandHandlerTests.cs    # 2 tests
    │   ├── Queries/
    │   │   └── GetCartQueryHandlerTests.cs        # 4 tests
    │   ├── Validators/
    │   │   └── CartValidatorTests.cs              # 13 tests
    │   └── Behaviors/
    │       └── ValidationBehaviorTests.cs         # 5 tests
    └── Infrastructure/
        ├── CartRepositoryTests.cs                 # 9 tests
        ├── UnitOfWorkTests.cs                     # 6 tests
        └── RedisSettingsTests.cs                  # 4 tests
```

---

## 6. Domain Layer Design

### 6.1 Aggregate Root: `Cart`

`Cart` is the single aggregate root. All state changes go through its public methods — no direct property setters are exposed outside the class.

```
Cart
├── Identity:   UserId (string, partition key)
├── Timeline:   CreatedAt (UTC), UpdatedAt (UTC)
├── Items:      IReadOnlyList<CartItem> (backed by private List<CartItem>)
├── Computed:   TotalAmount (sum Price × Quantity), TotalItems (sum Quantity)
└── Events:     _domainEvents (private List<object>)
```

**Factory methods:**

| Method | Description |
|--------|-------------|
| `Cart.Create(userId)` | Creates a new empty cart; sets `CreatedAt = UpdatedAt = UtcNow`; throws `ArgumentException` if `userId` is null/empty |
| `Cart.Restore(userId, createdAt, updatedAt, items)` | Reconstructs a cart from Redis snapshot; bypasses validation (called only by repository) |

**Behaviour methods:**

| Method | Signature | Description |
|--------|-----------|-------------|
| `AddItem` | `(productId, name, sku, price, qty, imageUrl?)` | Appends new `CartItem` or increments quantity on existing; updates `UpdatedAt`; raises `CartItemAddedEvent` |
| `RemoveItem` | `(productId)` | Removes the line item; throws `KeyNotFoundException` if absent; raises `CartItemRemovedEvent` |
| `UpdateItemQuantity` | `(productId, qty)` | Updates quantity; `qty ≤ 0` delegates to `RemoveItem`; throws `KeyNotFoundException` if absent |
| `Clear` | `()` | Clears `_items`; raises `CartClearedEvent`; updates `UpdatedAt` |
| `ClearDomainEvents` | `()` | Removes all raised events (called by handlers after processing) |

**Key invariants:**
- `TotalAmount` and `TotalItems` are always computed from `_items` — never stored directly.
- `UpdatedAt` is refreshed on every mutating call.
- Duplicate `ProductId` detection uses `_items.FirstOrDefault(i => i.ProductId == productId)`.

### 6.2 Entity: `CartItem`

`CartItem` represents a single product line in the cart.

| Property | Type | Access |
|----------|------|--------|
| `ProductId` | string | public, private set |
| `ProductName` | string | public, private set |
| `SKU` | string | public, private set |
| `Price` | decimal | public, private set |
| `Quantity` | int | public, private set |
| `ImageUrl` | string? | public, private set |

**Factory methods:**

| Method | Description |
|--------|-------------|
| `CartItem.Create(productId, productName, sku, price, qty, imageUrl?)` | Validates all required fields; throws `ArgumentException` on null/empty/invalid values |
| `CartItem.Restore(productId, productName, sku, price, qty, imageUrl?)` | Bypasses validation; used only by `CartRepository` during deserialisation |

**Internal method:**

| Method | Description |
|--------|-------------|
| `UpdateQuantity(int quantity)` | Sets `Quantity` — called by `Cart.AddItem` (merge) and `Cart.UpdateItemQuantity` |

### 6.3 Domain Events

Domain events are raised in-memory and stored on the aggregate root. They are designed for future pub/sub wiring (e.g., service bus integration).

| Event | Constructor | Raised When |
|-------|-------------|-------------|
| `CartItemAddedEvent` | `(string UserId, string ProductId, int Quantity)` | `Cart.AddItem()` succeeds (both new add and merge) |
| `CartItemRemovedEvent` | `(string UserId, string ProductId)` | `Cart.RemoveItem()` completes |
| `CartClearedEvent` | `(string UserId)` | `Cart.Clear()` completes |

All events are `public sealed record` types. They are cleared via `Cart.ClearDomainEvents()` after the handler processes them.

---

## 7. Application Layer Design

### 7.1 CQRS Commands

| Command | Input | Output | Handler Behaviour |
|---------|-------|--------|-------------------|
| `AddToCartCommand` | `string UserId`, `AddCartItemDto Item` | `CartDto` | Gets or creates cart via `IUnitOfWork.Carts.GetAsync` / `Cart.Create`; calls `AddItem`; saves; returns mapped DTO |
| `RemoveFromCartCommand` | `string UserId`, `string ProductId` | `CartDto` | Gets cart (404 if missing); calls `RemoveItem` (404 if item absent); saves; returns DTO |
| `UpdateCartItemCommand` | `string UserId`, `string ProductId`, `int Quantity` | `CartDto` | Gets cart (404 if missing); calls `UpdateItemQuantity`; saves; returns DTO |
| `ClearCartCommand` | `string UserId` | `bool` | Checks existence; if absent returns `false`; calls `Clear`; saves; returns `true` |

### 7.2 CQRS Queries

| Query | Input | Output | Handler Behaviour |
|-------|-------|--------|-------------------|
| `GetCartQuery` | `string UserId` | `CartDto?` | Calls `IUnitOfWork.Carts.GetAsync`; returns mapped DTO or `null` |

### 7.3 MediatR Pipeline

```
HTTP Request
    │
    ▼
Endpoint (IMediator.Send)
    │
    ▼
ValidationBehavior<TRequest, TResponse>   ← FluentValidation
    │  (throws ValidationException on failure → 400)
    ▼
Command/Query Handler
    │
    ▼
IUnitOfWork → ICartRepository → Redis
```

### 7.4 FluentValidation Rules

**AddToCartValidator:**

| Field | Rules |
|-------|-------|
| `UserId` | `NotEmpty`, `MaxLength(100)` |
| `Item.ProductId` | `NotEmpty` |
| `Item.ProductName` | `NotEmpty`, `MaxLength(200)` |
| `Item.SKU` | `NotEmpty`, `MaxLength(50)` |
| `Item.Price` | `GreaterThan(0)` |
| `Item.Quantity` | `GreaterThan(0)` |

**UpdateCartItemValidator:**

| Field | Rules |
|-------|-------|
| `UserId` | `NotEmpty` |
| `ProductId` | `NotEmpty` |
| `Quantity` | `GreaterThanOrEqualTo(0)` — allows 0 to signal removal |

### 7.5 DTOs

```csharp
// Add request payload
sealed record AddCartItemDto(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl = null);

// Update request payload
sealed record UpdateCartItemDto(int Quantity);

// Single line item in a cart response
sealed record CartItemDto(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl,
    decimal SubTotal);         // computed: Price * Quantity

// Full cart response
sealed record CartDto(
    string UserId,
    IReadOnlyList<CartItemDto> Items,
    decimal TotalAmount,
    int TotalItems,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

### 7.6 CartMapper

`CartMapper` is an `internal static` extension-method class with a single method:

```csharp
internal static CartDto ToDto(Cart cart)
```

It maps each `CartItem` to `CartItemDto`, computing `SubTotal = Price * Quantity`, then builds the `CartDto` from the aggregate's computed properties.

---

## 8. Infrastructure Layer Design

### 8.1 RedisSettings

| Property | Default | Description |
|----------|---------|-------------|
| `ConnectionString` | `localhost:6379` | Redis host and port |
| `InstanceName` | `AKCart:` | Namespace prefix for all keys |
| `CartExpiryDays` | `30` | Sliding TTL for cart keys |

Bound from `appsettings.json` section `"RedisSettings"` via `IOptions<RedisSettings>`.

### 8.2 RedisContext

`RedisContext` wraps `IConnectionMultiplexer` and exposes `IDatabase GetDatabase()`.

Two constructors support both production and test scenarios:

```csharp
// Production: creates real Redis connection from settings
public RedisContext(IOptions<RedisSettings> settings)
{
    _multiplexer = ConnectionMultiplexer.Connect(settings.Value.ConnectionString);
}

// Tests: injects a mock IConnectionMultiplexer (bypasses real connection)
public RedisContext(IConnectionMultiplexer multiplexer)
{
    _multiplexer = multiplexer;
}
```

Registered as **Singleton** in DI (one multiplexer per process is the StackExchange.Redis recommendation).

### 8.3 CartRepository

Implements `ICartRepository`. Serialises cart state as a JSON snapshot using `System.Text.Json`.

**Private snapshot records (internal serialisation contract):**

```csharp
private sealed record CartSnapshot(
    string UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<CartItemSnapshot> Items);

private sealed record CartItemSnapshot(
    string ProductId,
    string ProductName,
    string SKU,
    decimal Price,
    int Quantity,
    string? ImageUrl);
```

These records are never exposed outside the repository — they exist solely to enable clean JSON serialisation of domain objects that have private setters.

**Key format:**

```csharp
$"{_settings.InstanceName}cart:{userId}"
// Example: "AKCart:cart:user-001"
```

**Operations:**

| Method | Redis Call | Description |
|--------|-----------|-------------|
| `GetAsync(userId, ct)` | `StringGetAsync(key)` | Deserialises snapshot → `Cart.Restore()` → returns `Cart?` |
| `SaveAsync(cart, ct)` | `StringSetAsync(key, json, TTL)` | Serialises to `CartSnapshot` → stores with `TimeSpan.FromDays(CartExpiryDays)` |
| `DeleteAsync(userId, ct)` | `KeyDeleteAsync(key)` | Removes key; returns `bool` indicating whether key existed |
| `ExistsAsync(userId, ct)` | `KeyExistsAsync(key)` | Returns `bool` |

**Two constructors** (same pattern as `RedisContext`):

```csharp
// Production: uses RedisContext to obtain IDatabase
public CartRepository(RedisContext context, IOptions<RedisSettings> settings)

// Tests: accepts IDatabase directly (mock-friendly)
public CartRepository(IDatabase database, IOptions<RedisSettings> settings)
```

### 8.4 UnitOfWork

| Member | Description |
|--------|-------------|
| `ICartRepository Carts` | Lazily initialised: `_carts ??= new CartRepository(_context, _settings)` |
| `Task<int> SaveChangesAsync(ct)` | Always returns `Task.FromResult(1)` — Redis auto-commits each write |
| `Dispose()` | Frees the `CartRepository` reference |

Registered as **Scoped** in DI — one instance per HTTP request.

### 8.5 Infrastructure DI Registration

```csharp
services.Configure<RedisSettings>(configuration.GetSection("RedisSettings"));
services.AddSingleton<RedisContext>();
services.AddScoped<ICartRepository, CartRepository>();
services.AddScoped<IUnitOfWork, UnitOfWork>();
```

---

## 9. API Layer Design

### 9.1 Minimal API Endpoints

All endpoints are grouped under `/api/v1/cart` and registered via `CartEndpoints.MapCartEndpoints(app)`.

| Method | Route | Handler | Success Response |
|--------|-------|---------|-----------------|
| GET    | `/api/v1/cart/{userId}` | `GetCartQuery` | 200 `CartDto` / 404 if not found |
| POST   | `/api/v1/cart/{userId}/items` | `AddToCartCommand` | 200 `CartDto` |
| PUT    | `/api/v1/cart/{userId}/items/{productId}` | `UpdateCartItemCommand` | 200 `CartDto` |
| DELETE | `/api/v1/cart/{userId}/items/{productId}` | `RemoveFromCartCommand` | 200 `CartDto` |
| DELETE | `/api/v1/cart/{userId}` | `ClearCartCommand` | 204 No Content |

### 9.2 Endpoint Binding

- `userId` and `productId` are bound from route segments.
- Request bodies (`AddCartItemDto`, `UpdateCartItemDto`) are bound from the JSON body.
- Validation failures are caught by `ValidationBehavior` before handlers run.

### 9.3 Error Response Format

```json
// 400 Bad Request (validation failure)
{
  "errors": [
    { "propertyName": "Item.Price", "errorMessage": "'Item Price' must be greater than '0'." }
  ]
}

// 404 Not Found
{ "error": "Cart for user 'user-001' not found" }

// 409 Conflict (business rule violation)
{ "error": "..." }

// 500 Internal Server Error
{ "error": "An unexpected error occurred" }
```

### 9.4 ExceptionHandlerMiddleware

Registered first in the middleware pipeline. Maps exceptions to HTTP status codes:

| Exception | HTTP Status Code |
|-----------|-----------------|
| `FluentValidation.ValidationException` | 400 Bad Request |
| `KeyNotFoundException` | 404 Not Found |
| `InvalidOperationException` | 409 Conflict |
| `Exception` (catch-all) | 500 Internal Server Error |

### 9.5 Program.cs Startup Order

```csharp
builder.AddSerilogLogging();              // AK.BuildingBlocks structured logging
builder.Services.AddApplication();        // MediatR, Validators, ValidationBehavior
builder.Services.AddInfrastructure(cfg);  // RedisSettings, RedisContext, UoW
builder.Services.AddDefaultHealthChecks(); // /health endpoint
builder.Services.AddSwaggerGen(...);

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.MapCartEndpoints();
app.MapDefaultHealthChecks();             // AK.BuildingBlocks health check
```

---

## 10. Data Model

### Redis Key Structure

| Key Pattern | Example | TTL |
|-------------|---------|-----|
| `{InstanceName}cart:{userId}` | `AKCart:cart:user-001` | 30 days (sliding) |

### JSON Snapshot Schema

The value stored at each key is a JSON-serialised `CartSnapshot`:

```json
{
  "UserId": "user-001",
  "CreatedAt": "2026-04-01T10:00:00Z",
  "UpdatedAt": "2026-04-17T14:32:00Z",
  "Items": [
    {
      "ProductId": "prod-001",
      "ProductName": "Men's Classic Oxford Shirt",
      "SKU": "MEN-SHIR-001",
      "Price": 1299.99,
      "Quantity": 2,
      "ImageUrl": null
    },
    {
      "ProductId": "prod-042",
      "ProductName": "Men's Slim Fit Chinos",
      "SKU": "MEN-PANT-002",
      "Price": 999.00,
      "Quantity": 1,
      "ImageUrl": "https://cdn.example.com/images/prod-042.jpg"
    }
  ]
}
```

### Computed Fields (not stored)

The following are derived at read time and never persisted to Redis:

| Field | Computation |
|-------|-------------|
| `CartItemDto.SubTotal` | `Price * Quantity` per item |
| `CartDto.TotalAmount` | `Items.Sum(i => i.Price * i.Quantity)` |
| `CartDto.TotalItems` | `Items.Sum(i => i.Quantity)` |

---

## 11. CQRS & MediatR Pipeline

```
Request (IRequest<TResponse>)
    │
    ├── ValidationBehavior<TRequest, TResponse>
    │     Collects all IValidator<TRequest> from DI
    │     Runs all validators in parallel
    │     Throws FluentValidation.ValidationException if any rule fails
    │
    └── IRequestHandler<TRequest, TResponse>
          (AddToCartCommandHandler, GetCartQueryHandler, etc.)
```

**Registration (`AddApplication`):**

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
services.AddValidatorsFromAssembly(assembly);
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

**Behaviour when no validator is registered:**
`ValidationBehavior` checks `IEnumerable<IValidator<TRequest>>` — if empty, it proceeds directly to the handler without throwing.

---

## 12. Redis Storage Strategy

### Snapshot Pattern

Rather than mapping Redis hashes or sorted sets to domain objects, cart state is stored as a single JSON string per cart. This approach:

- Reads and writes the full cart in one Redis round-trip
- Avoids complex multi-key transactions
- Keeps serialisation logic entirely within the repository
- Allows the domain model to remain clean (private setters, factory methods)

### Serialisation Boundary

```
Domain (Cart / CartItem)
    │  CartRepository.SaveAsync
    ▼
CartSnapshot / CartItemSnapshot   ←── private records, serialisation contract
    │  System.Text.Json
    ▼
Redis (UTF-8 JSON string)

Redis
    │  System.Text.Json
    ▼
CartSnapshot / CartItemSnapshot
    │  Cart.Restore() / CartItem.Restore()
    ▼
Domain (Cart / CartItem)
```

The snapshot records mirror the domain model fields but are decoupled — changes to domain validation or factory logic do not affect the stored format as long as field names are preserved.

### TTL (Sliding Window)

Every `SaveAsync` call sets the key TTL to `CartExpiryDays` days from `UtcNow`. This creates a sliding 30-day window: an active cart never expires, but an abandoned cart is cleaned up automatically after 30 days of inactivity.

---

## 13. Unit of Work Pattern

The Unit of Work provides a consistent interface for data access regardless of the underlying store:

```
Handler → IUnitOfWork.Carts.SaveAsync(cart)
        → IUnitOfWork.SaveChangesAsync()  // returns 1 (no-op for Redis)
```

Benefits:
- Handlers depend on `IUnitOfWork` (interface) — testable via `Mock<IUnitOfWork>`.
- Consistent pattern with other AntKart services (AK.Products uses the same interface shape).
- Future transition to a different store (e.g., PostgreSQL) requires only Infrastructure changes — Application handlers are unchanged.

`SaveChangesAsync` returns `Task.FromResult(1)` because Redis auto-commits each write. This is documented as a no-op convention, not a hidden side-effect.

---

## 14. API Reference

### Base URL: `http://localhost:5079` (dev) / `http://localhost:8082` (Docker)

#### Read Endpoints

```
GET /api/v1/cart/{userId}
```

Returns `CartDto` for the user, or `404` if no cart exists.

#### Write Endpoints

```
POST   /api/v1/cart/{userId}/items              → 200 CartDto
PUT    /api/v1/cart/{userId}/items/{productId}  → 200 CartDto
DELETE /api/v1/cart/{userId}/items/{productId}  → 200 CartDto
DELETE /api/v1/cart/{userId}                    → 204 No Content
```

#### Health Check

```
GET /health
```

Returns `200 OK` with `{"status": "Healthy"}` when the service is running.

#### Swagger UI

```
GET /swagger
```

Available in both Development and Production environments.

#### Request Bodies

**POST `/api/v1/cart/{userId}/items`**
```json
{
  "productId": "prod-001",
  "productName": "Men's Classic Oxford Shirt",
  "sku": "MEN-SHIR-001",
  "price": 1299.99,
  "quantity": 1,
  "imageUrl": null
}
```

**PUT `/api/v1/cart/{userId}/items/{productId}`**
```json
{
  "quantity": 3
}
```

#### Sample Response (`CartDto`)

```json
{
  "userId": "user-001",
  "items": [
    {
      "productId": "prod-001",
      "productName": "Men's Classic Oxford Shirt",
      "sku": "MEN-SHIR-001",
      "price": 1299.99,
      "quantity": 2,
      "imageUrl": null,
      "subTotal": 2599.98
    }
  ],
  "totalAmount": 2599.98,
  "totalItems": 2,
  "createdAt": "2026-04-17T10:00:00Z",
  "updatedAt": "2026-04-17T14:32:00Z"
}
```

---

## 15. Testing Strategy

### Test Pyramid

```
     ┌─────────────────┐
     │  Integration /  │  (Future — requires running Redis)
     │  E2E Tests      │
     ├─────────────────┤
     │  Unit Tests     │  ← Current scope (XUnit + Moq)
     │  (88 tests)     │
     └─────────────────┘
```

### Test Coverage by Layer

| Test Class | Tests | What is Covered |
|---|---|---|
| `CartTests` | 16 | `Create`, `Restore`, `AddItem` (new/merge), `RemoveItem` (found/not-found), `UpdateItemQuantity` (positive/zero/not-found), `Clear`, `ClearDomainEvents`, `TotalAmount`, `TotalItems` |
| `CartItemTests` | 9 | `Create` (happy path + all invalid inputs), `Restore` (skips validation), `UpdateQuantity` |
| `AddToCartCommandHandlerTests` | 3 | New cart created; item added to existing cart; duplicate product merges quantity |
| `RemoveFromCartCommandHandlerTests` | 3 | Happy path; cart not found (404); item not found (404) |
| `UpdateCartItemCommandHandlerTests` | 3 | Valid quantity update; zero quantity removes item; cart not found (404) |
| `ClearCartCommandHandlerTests` | 2 | Clears existing cart (returns true); cart not found (returns false) |
| `GetCartQueryHandlerTests` | 4 | Cart found; cart not found (null); totals computed correctly; empty cart |
| `CartValidatorTests` | 13 | All field rules for `AddToCartValidator` and `UpdateCartItemValidator` |
| `ValidationBehaviorTests` | 5 | No validators registered; single passing validator; single failing validator; multi-validator; error format |
| `CartRepositoryTests` | 9 | `GetAsync` (found / not-found / null Redis value); `SaveAsync` (key format, serialisation, TTL); `DeleteAsync`; `ExistsAsync` (true/false); key prefix verification |
| `UnitOfWorkTests` | 6 | Repository lazy initialisation; same instance on second access; `SaveChangesAsync` returns 1; `Dispose` clears repo reference; repository type is correct |
| `RedisSettingsTests` | 4 | Default values; property setters; `CartExpiryDays` default 30; `InstanceName` default `AKCart:` |
| **Total** | **88** | |

### Test Tooling

| Tool | Version | Purpose |
|------|---------|---------|
| xUnit | 2.9.2 | Test runner |
| Moq | 4.20.72 | Mock `IUnitOfWork`, `ICartRepository`, `IDatabase`, `IConnectionMultiplexer` |
| FluentAssertions | 7.2.0 | Readable assertion DSL |
| coverlet.collector | 6.0.2 | Code coverage collection |
| `TestDataFactory` | — | Reusable test data: `CreateEmptyCart()`, `CreateCartWithItem()`, `CreateCartWithMultipleItems()`, `CreateAddCartItemDto()`, `CreateCartItem()` |

### Notable Test Patterns

**Mocking `IDatabase` (StackExchange.Redis):**

```csharp
var db = new Mock<IDatabase>();
db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
  .ReturnsAsync(RedisValue.Null);
var repo = new CartRepository(db.Object, Options.Create(new RedisSettings()));
```

**ValidationBehavior with concrete validators (avoids Moq limitations):**

```csharp
public sealed class CartAlwaysFailValidator : AbstractValidator<CartBehaviorTestRequest>
{
    public CartAlwaysFailValidator() =>
        RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
}
```

**Handler tests with `Mock<IUnitOfWork>`:**

```csharp
_uow.Setup(u => u.Carts.GetAsync("user-001", default)).ReturnsAsync((Cart?)null);
_uow.Setup(u => u.Carts.SaveAsync(It.IsAny<Cart>(), default)).Returns(Task.CompletedTask);
_uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);
```

---

## 16. Configuration & Deployment

### appsettings.json

```json
{
  "RedisSettings": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "AKCart:",
    "CartExpiryDays": 30
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Docker Compose

```yaml
redis:
  image: redis:7-alpine
  container_name: antcart-redis
  restart: unless-stopped
  ports:
    - "6379:6379"

ak-shoppingcart-api:
  image: ak-shoppingcart-api
  build:
    context: .
    dockerfile: AK.ShoppingCart/AK.ShoppingCart.API/Dockerfile
  container_name: ak-shoppingcart-api
  restart: unless-stopped
  ports:
    - "8082:8080"
  environment:
    - ASPNETCORE_ENVIRONMENT=Production
    - RedisSettings__ConnectionString=redis:6379
    - RedisSettings__InstanceName=AKCart:
    - RedisSettings__CartExpiryDays=30
  depends_on:
    - redis
```

### Dockerfile (multi-stage)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj AK.BuildingBlocks/AK.BuildingBlocks/
COPY AK.ShoppingCart/AK.ShoppingCart.Domain/AK.ShoppingCart.Domain.csproj AK.ShoppingCart/AK.ShoppingCart.Domain/
COPY AK.ShoppingCart/AK.ShoppingCart.Application/AK.ShoppingCart.Application.csproj AK.ShoppingCart/AK.ShoppingCart.Application/
COPY AK.ShoppingCart/AK.ShoppingCart.Infrastructure/AK.ShoppingCart.Infrastructure.csproj AK.ShoppingCart/AK.ShoppingCart.Infrastructure/
COPY AK.ShoppingCart/AK.ShoppingCart.API/AK.ShoppingCart.API.csproj AK.ShoppingCart/AK.ShoppingCart.API/
RUN dotnet restore AK.ShoppingCart/AK.ShoppingCart.API/AK.ShoppingCart.API.csproj
COPY . .
RUN dotnet publish AK.ShoppingCart/AK.ShoppingCart.API/AK.ShoppingCart.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AK.ShoppingCart.API.dll"]
```

### Running Locally

```bash
# Prerequisites: Redis running on localhost:6379
# Option A: Docker
docker run -d -p 6379:6379 redis:7-alpine

# Option B: via Docker Compose (all services)
docker-compose up --build

# Run individually (dev)
cd AK.ShoppingCart/AK.ShoppingCart.API && dotnet run
# → http://localhost:5079/swagger

# Run tests
dotnet test AK.ShoppingCart/AK.ShoppingCart.Tests/AK.ShoppingCart.Tests.csproj --verbosity normal

# Run tests with coverage
dotnet test AK.ShoppingCart/AK.ShoppingCart.Tests/AK.ShoppingCart.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage
```

---

## 17. Design Decisions & Trade-offs

| Decision | Rationale | Trade-off |
|----------|-----------|-----------|
| **Redis (not a relational DB)** | Sub-millisecond cart reads are critical for checkout flow; Redis TTL handles abandoned cart cleanup automatically | No complex queries; no relationships; JSON snapshots grow with cart size |
| **JSON snapshot (not Redis Hash)** | One round-trip per operation; no field-level updates needed; simpler deserialisation | Writes the entire cart on every mutation, even for a single field change |
| **Private snapshot records** | Keeps domain model clean (private setters, factory methods) while enabling JSON serialisation | Two parallel models to maintain (domain vs. snapshot); snapshot field names must stay stable |
| **Sliding TTL on every save** | Active carts never expire; abandoned carts are cleaned up after 30 days without code | Slightly higher Redis write overhead compared to a fixed TTL |
| **Implicit cart creation** | Fewer API calls for consumers (no "create cart" step); better developer experience | Cart is created on first add — no way to distinguish "user has never shopped" from "cart expired" |
| **Minimal API (not Controllers)** | Lower overhead, less boilerplate, idiomatic .NET 9 | Slightly less structure for large teams |
| **CQRS with MediatR** | Clear separation of reads/writes; easy to add pipeline behaviors (e.g., logging, caching) | Additional indirection vs direct service calls |
| **Unit of Work (no-op save)** | Consistent interface across all AntKart services; testable via `Mock<IUnitOfWork>` | `SaveChangesAsync` is a no-op — could mislead developers; documented explicitly |
| **Testable constructors (`IDatabase` injection)** | No in-memory Redis driver exists; `IDatabase` is an interface in StackExchange.Redis | Test constructor bypasses real connection — production path differs slightly |
| **Static `CartMapper`** | No AutoMapper dependency; simple field mapping | Manual sync when model changes; no auto-sync |
| **Domain events in-memory only** | Prepares for future pub/sub without adding broker complexity in current scope | Events are raised but never published — must wire up a bus for downstream consumers |
