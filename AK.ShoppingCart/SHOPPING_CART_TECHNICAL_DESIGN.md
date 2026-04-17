# AK.ShoppingCart — Technical Design Document

## Overview

AK.ShoppingCart is a REST Minimal API microservice responsible for managing shopping carts within the AntKart platform. It stores cart data in **Redis** for low-latency reads/writes and supports full cart lifecycle: add items, update quantities, remove items, and clear the cart.

---

## Architecture

The service follows the same **DDD + Clean Architecture** pattern as other AntKart microservices.

```
AK.ShoppingCart/
├── AK.ShoppingCart.Domain/          Domain entities and events
├── AK.ShoppingCart.Application/     CQRS handlers, DTOs, validators, interfaces
├── AK.ShoppingCart.Infrastructure/  Redis persistence, repository
├── AK.ShoppingCart.API/             Minimal API endpoints, middleware
└── AK.ShoppingCart.Tests/           Unit tests (88 tests)
```

**Layer dependency rules:**
```
Domain           ← no dependencies
Application      ← Domain, AK.BuildingBlocks
Infrastructure   ← Application (Domain transitively)
API              ← Application, Infrastructure, AK.BuildingBlocks
Tests            ← Application, Infrastructure, Domain, AK.BuildingBlocks
```

---

## Transport & Configuration

| Property | Value |
|----------|-------|
| Protocol | HTTP REST (Minimal API) |
| Dev port | 5079 |
| Docker port | 8082 |
| Health endpoint | `/health` |
| Swagger | `/swagger` |

**appsettings:**
```json
{
  "RedisSettings": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "AKCart:",
    "CartExpiryDays": 30
  }
}
```

---

## Domain Model

### Cart (Aggregate Root)

| Property | Type | Description |
|----------|------|-------------|
| UserId | string | Identifies whose cart this is (partition key) |
| Items | IReadOnlyList\<CartItem\> | Line items in the cart |
| TotalAmount | decimal | Computed: sum of Price × Quantity |
| TotalItems | int | Computed: sum of all Quantity values |
| CreatedAt | DateTime | UTC creation timestamp |
| UpdatedAt | DateTime | UTC last-modified timestamp |

**Factory methods:**
- `Cart.Create(userId)` — creates a new empty cart; throws `ArgumentException` if userId is empty
- `Cart.Restore(userId, createdAt, updatedAt, items)` — reconstructs cart from Redis storage

**Behaviour methods:**

| Method | Description |
|--------|-------------|
| `AddItem(productId, name, sku, price, qty, imageUrl?)` | Adds a new item; if product already in cart, increments quantity |
| `RemoveItem(productId)` | Removes item; throws `KeyNotFoundException` if not found |
| `UpdateItemQuantity(productId, qty)` | Updates quantity; qty ≤ 0 removes the item; throws if not found |
| `Clear()` | Removes all items |
| `ClearDomainEvents()` | Clears the internal domain event list |

### CartItem (Entity)

| Property | Type |
|----------|------|
| ProductId | string |
| ProductName | string |
| SKU | string |
| Price | decimal |
| Quantity | int |
| ImageUrl | string? |

**Factory:** `CartItem.Create(...)` — validates all required fields, throws `ArgumentException` on invalid data.  
**Restore:** `CartItem.Restore(...)` — used by repository for deserialisation, skips validation.

### Domain Events

| Event | Raised when |
|-------|-------------|
| `CartItemAddedEvent(UserId, ProductId, Quantity)` | Item added/quantity merged |
| `CartItemRemovedEvent(UserId, ProductId)` | Item explicitly removed |
| `CartClearedEvent(UserId)` | Cart cleared |

---

## CQRS Operations

### Commands

| Command | Returns | Description |
|---------|---------|-------------|
| `AddToCartCommand(UserId, AddCartItemDto)` | `CartDto` | Creates cart if missing, then adds/merges item |
| `RemoveFromCartCommand(UserId, ProductId)` | `CartDto` | Removes item; 404 if cart or item not found |
| `UpdateCartItemCommand(UserId, ProductId, Quantity)` | `CartDto` | Updates qty; qty=0 removes item; 404 if not found |
| `ClearCartCommand(UserId)` | `bool` | Clears cart; returns false if cart doesn't exist |

### Queries

| Query | Returns | Description |
|-------|---------|-------------|
| `GetCartQuery(UserId)` | `CartDto?` | Returns cart or null if not found |

### Validation (FluentValidation)

**AddToCartValidator:**
- UserId: `NotEmpty`, `MaxLength(100)`
- Item.ProductId: `NotEmpty`
- Item.ProductName: `NotEmpty`, `MaxLength(200)`
- Item.SKU: `NotEmpty`, `MaxLength(50)`
- Item.Price: `GreaterThan(0)`
- Item.Quantity: `GreaterThan(0)`

**UpdateCartItemValidator:**
- UserId: `NotEmpty`
- ProductId: `NotEmpty`
- Quantity: `GreaterThanOrEqualTo(0)` (0 = remove item)

---

## DTOs

```csharp
record AddCartItemDto(string ProductId, string ProductName, string SKU, decimal Price, int Quantity, string? ImageUrl);
record UpdateCartItemDto(int Quantity);
record CartItemDto(string ProductId, string ProductName, string SKU, decimal Price, int Quantity, string? ImageUrl, decimal SubTotal);
record CartDto(string UserId, IReadOnlyList<CartItemDto> Items, decimal TotalAmount, int TotalItems, DateTime CreatedAt, DateTime UpdatedAt);
```

---

## Infrastructure — Redis

### RedisSettings

| Property | Default | Description |
|----------|---------|-------------|
| ConnectionString | `localhost:6379` | Redis server |
| InstanceName | `AKCart:` | Key namespace prefix |
| CartExpiryDays | `30` | TTL for cart keys |

**Redis key format:** `{InstanceName}cart:{userId}` — e.g. `AKCart:cart:user-001`

### RedisContext

Wraps `IConnectionMultiplexer`. Provides a testable constructor accepting `IConnectionMultiplexer` directly (bypasses real connection for unit tests).

### CartRepository

Uses `System.Text.Json` for serialisation. Cart state is stored as a JSON snapshot (`CartSnapshot` / `CartItemSnapshot` internal records), then reconstructed via `Cart.Restore()` and `CartItem.Restore()` on reads.

Operations:
- `GetAsync` → `StringGetAsync` + deserialise
- `SaveAsync` → serialise + `StringSetAsync` with TTL
- `DeleteAsync` → `KeyDeleteAsync`
- `ExistsAsync` → `KeyExistsAsync`

### UnitOfWork

Lazily initialises `CartRepository`. `SaveChangesAsync` always returns 1 (Redis auto-commits on each operation).

---

## API Endpoints

Base path: `/api/v1/cart`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/{userId}` | Get user cart |
| POST | `/{userId}/items` | Add item to cart |
| PUT | `/{userId}/items/{productId}` | Update item quantity |
| DELETE | `/{userId}/items/{productId}` | Remove item from cart |
| DELETE | `/{userId}` | Clear entire cart |

### Error Mapping

| Exception | HTTP Status |
|-----------|-------------|
| `ValidationException` | 400 Bad Request |
| `KeyNotFoundException` | 404 Not Found |
| `InvalidOperationException` | 409 Conflict |
| `Exception` (catch-all) | 500 Internal Server Error |

---

## Tests (88 tests)

| Area | Tests | Coverage |
|------|-------|----------|
| Domain — Cart | 16 | All entity behaviour paths |
| Domain — CartItem | 9 | All factory and restore paths |
| Application — AddToCart | 3 | New cart, existing cart, same product merge |
| Application — RemoveFromCart | 3 | Happy path, cart not found, item not found |
| Application — UpdateCartItem | 3 | Valid update, zero qty, cart not found |
| Application — ClearCart | 2 | Clear existing, cart not found |
| Application — GetCart | 4 | Found, not found, totals, empty |
| Application — Validators | 13 | All field rules for both validators |
| Application — ValidationBehavior | 5 | No validators, pass, fail, multi-validator |
| Infrastructure — CartRepository | 9 | Get/save/delete/exists + key prefix |
| Infrastructure — UnitOfWork | 6 | Lazy repo, save returns 1, dispose |
| Infrastructure — RedisSettings | 4 | Defaults and property setters |

---

## Docker

```yaml
ak-shoppingcart-api:
  image: ak-shoppingcart-api
  build:
    context: .
    dockerfile: AK.ShoppingCart/AK.ShoppingCart.API/Dockerfile
  ports:
    - "8082:8080"
  environment:
    - RedisSettings__ConnectionString=redis:6379
  depends_on:
    - redis

redis:
  image: redis:7-alpine
  ports:
    - "6379:6379"
```

---

## Running Locally

```bash
# Requires Redis running locally
cd AK.ShoppingCart/AK.ShoppingCart.API && dotnet run
# → http://localhost:5079/swagger
```
