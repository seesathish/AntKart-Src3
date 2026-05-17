# Skill: Verify IDOR Safety

**Purpose:** Audit all endpoints in a service (or a single new endpoint) to confirm they follow AntKart's IDOR-safe patterns: no `userId` in route paths or request DTOs, JWT-derived identity, ownership checks on single-resource operations, and admin-only policy on admin routes.

---

## What Is IDOR?

Insecure Direct Object Reference (IDOR) occurs when an API accepts a user-controlled identifier (e.g. `userId` in the URL or request body) and uses it to scope data access without verifying it matches the authenticated caller. An attacker passes another user's ID to read or modify their data.

**Example of the vulnerability (old pattern — never do this):**
```
GET /api/orders/user/{userId}       ← userId controlled by caller — IDOR
DELETE /api/cart/{userId}           ← same problem
POST /api/payments { "userId": "x"} ← body injection
```

**AntKart's fixed pattern:**
```
GET /api/orders/me                  ← no userId in path; derived from JWT
DELETE /api/cart                    ← no userId in path
POST /api/payments { orderId, ... } ← no userId in body; injected from JWT in handler
```

---

## Step 1 — Scan Endpoint Paths for userId

```bash
# Search all endpoint files in a service for userId in route patterns
grep -n "userId\|user_id\|UserId" AK.<Service>/AK.<Service>.API/Endpoints/*.cs

# Search all endpoint files across all services
grep -rn "{userId}\|{user_id}" --include="*.cs" .
```

**Expected result:** Zero matches for `{userId}` in any route template. Any match is a potential IDOR vulnerability.

---

## Step 2 — Scan Request DTOs for userId Fields

```bash
# Search Application layer DTOs and request records for userId fields
grep -rn "UserId\|userId\|user_id" AK.<Service>/AK.<Service>.Application/ --include="*.cs" \
  | grep -i "record\|class\|property\|public"
```

**Expected result:** No user-controlled `userId` field in any DTO that is accepted as input. `UserId` fields are allowed in *response* DTOs (for display) and in *command* records (where the value is injected from JWT in the endpoint, not from the client request body).

---

## Step 3 — Verify GetUserId() Usage in Endpoints

```bash
# Every user-scoped endpoint must call http.GetUserId()
grep -n "GetUserId\(\)\|GetUserEmail\(\)\|GetUserDisplayName\(\)" \
  AK.<Service>/AK.<Service>.API/Endpoints/*.cs
```

Check every endpoint that returns or mutates user-specific data:
- [ ] Calls `var userId = http.GetUserId();`
- [ ] Passes `userId` to the MediatR command/query (not from the request body)
- [ ] Does NOT read userId from route params, query string, or request body

---

## Step 4 — Verify Ownership Checks on Single-Resource Operations

For `GET /{id}`, `DELETE /{id}`, `PUT /{id}` endpoints, the handler (or endpoint) must verify the resource belongs to the caller:

```csharp
// Pattern 1: Handler throws UnauthorizedAccessException → middleware maps to 403
var order = await orderRepository.GetByIdAsync(id, ct)
    ?? throw new KeyNotFoundException($"Order {id} not found.");
if (order.UserId != request.UserId && !request.IsAdmin)
    throw new UnauthorizedAccessException("You do not own this order.");

// Pattern 2: Endpoint catches and returns Forbid()
try {
    var notification = await mediator.Send(new GetNotificationByIdQuery(id, userId));
    return notification is null ? Results.NotFound() : Results.Ok(notification);
} catch (UnauthorizedAccessException) {
    return Results.Forbid();
}
```

Run this check:
```bash
grep -n "UnauthorizedAccessException\|Results.Forbid\|IsInRole\|isAdmin" \
  AK.<Service>/AK.<Service>.API/Endpoints/*.cs
```

For every endpoint that takes an `{id:guid}` parameter, confirm one of these ownership patterns is present in either the endpoint or the handler.

---

## Step 5 — Verify Admin-Only Routes

Admin routes must use `RequireAuthorization("admin")`, not just `RequireAuthorization("authenticated")`.

```bash
# Find all admin-scoped endpoints
grep -n "admin\|Admin" AK.<Service>/AK.<Service>.API/Endpoints/*.cs \
  | grep -i "require\|map\|policy"
```

Confirm:
- Admin routes are registered with `.RequireAuthorization("admin")`
- They are **not** on the shared group that uses `.RequireAuthorization("authenticated")`
- No admin route is accidentally using only `"authenticated"` (which regular users can pass)

---

## Step 6 — Live Integration Test

With Docker running, test that cross-user access is blocked:

```bash
# Get tokens for two users
USER1_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

USER2_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user2","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# 1. Create a resource as User1 (e.g. an order) and note the ID
# 2. Try to access it as User2
RESOURCE_ID="<id-belonging-to-user1>"
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  http://localhost:8000/gateway/<service>/$RESOURCE_ID \
  -H "Authorization: Bearer $USER2_TOKEN")
echo "Cross-user access: $CODE (expect 403)"

# 3. Try injecting userId in body
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  -X POST http://localhost:8000/gateway/<service>/create \
  -H "Authorization: Bearer $USER2_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"userId":"<user1-id>", ...}' )
echo "Body userId injection: $CODE (expect 400 — field not recognized — or 201 using USER2 sub)"

# 4. Regular user hits admin endpoint
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  http://localhost:8000/gateway/<service>/admin \
  -H "Authorization: Bearer $USER1_TOKEN")
echo "User on admin route: $CODE (expect 403)"
```

---

## Step 7 — Check ExceptionHandlerMiddleware Mapping

Confirm `UnauthorizedAccessException` maps to 403 (not 401) in the service's middleware.

```bash
grep -n "UnauthorizedAccessException" \
  AK.<Service>/AK.<Service>.API/Middleware/ExceptionHandlerMiddleware.cs \
  AK.BuildingBlocks/AK.BuildingBlocks/Middleware/ExceptionHandlerMiddleware.cs
```

Expected:
```csharp
UnauthorizedAccessException => StatusCodes.Status403Forbidden
```

**Note:** `UserIdentity` has its own middleware that maps `UnauthorizedAccessException` → 401 (correct for auth failures). All other services use the shared BuildingBlocks middleware that maps it → 403 (correct for ownership failures).

---

## IDOR-Safe Route Reference

| Service | Pattern | Notes |
|---------|---------|-------|
| ShoppingCart | `GET /api/v1/cart` | No userId in path |
| ShoppingCart | `POST /api/v1/cart/items` | No userId in body |
| Order | `GET /api/orders/me` | `/me` replaces `/{userId}` |
| Order | `GET /api/orders/{id}` | Ownership check in handler |
| Order | `DELETE /api/orders/{id}` | Ownership check in handler |
| Order | `PUT /api/orders/{id}/status` | Admin only |
| Payments | `GET /api/payments/me` | No userId in path |
| SavedCards | `GET /api/payments/cards` | No userId in path |
| SavedCards | `DELETE /api/payments/cards/{id}` | Ownership check in handler |
| Notifications | `GET /api/notifications` | JWT-scoped in handler |
| Notifications | `GET /api/notifications/{id}` | Ownership check + admin bypass |
| Notifications | `GET /api/notifications/admin` | Admin only |

---

## Checklist

- [ ] No `{userId}` in any route path template
- [ ] No `userId` / `UserId` field in any request DTO or command record (input side)
- [ ] Every user-scoped endpoint calls `http.GetUserId()` and passes it to the handler
- [ ] Every single-resource GET/DELETE/PUT verifies `resource.UserId == callerUserId` (or admin bypass)
- [ ] Admin-only routes use `.RequireAuthorization("admin")`
- [ ] `UnauthorizedAccessException` maps to 403 in middleware
- [ ] Cross-user live test returns 403
- [ ] Body userId injection test: field ignored or 400 (not 201 with spoofed userId)
