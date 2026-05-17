# AntKart — Security Test Guide

This document covers ethical black-box and grey-box security tests for the AntKart platform. All tests are run against the live Docker Compose stack.

**Prerequisites:** Docker Compose stack running (`docker-compose up -d`), `curl` available, Python 3 available.

---

## Setup

```bash
# Start the stack
docker-compose up -d

# Register two test accounts (run once)
curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","email":"sectest@example.com","password":"SecTest@123","firstName":"Sec","lastName":"Test"}'

curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user2","email":"sectest2@example.com","password":"SecTest@123","firstName":"Sec2","lastName":"Test2"}'

# Fetch tokens (re-run at the start of each test session — tokens expire)
USER1_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

USER2_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user2","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")
```

---

## Test Results Summary

| # | Category | Last Result | Expected |
|---|----------|-------------|----------|
| 1 | Unauthenticated access | ✅ PASS | 401 on all protected routes |
| 2 | JWT tampering / alg:none | ✅ PASS | 401 on forged tokens |
| 3 | Privilege escalation | ✅ PASS | 403 for regular user on admin routes |
| 4 | IDOR — Orders | ✅ PASS | 403 cross-user order access |
| 4b | IDOR — Payments | ✅ PASS | 403 cross-user payment access |
| 5 | Body injection (register) | ✅ PASS | Extra fields silently ignored |
| 6 | Cart userId spoofing | ✅ PASS | JWT sub used; injected field ignored |
| 7 | Input validation | ✅ PASS | 400 on invalid amounts |
| 8 | Information disclosure | ✅ PASS | No stack traces in error responses |
| 9 | Gateway rate limiting | ✅ PASS | 429 after threshold exceeded |
| 10 | HTTP verb tampering | ✅ PASS | 405 on wrong verbs |
| 11 | Brute force login | ✅ PASS | Account locked after 5 failed attempts |
| 12 | Server header exposure | ✅ PASS | No `Server` header in responses |
| 13 | Direct service bypass | ✅ PASS | Auth enforced on all services |
| 14 | Mass assignment (Payments) | ✅ PASS | Extra body fields ignored |
| 15 | Notification IDOR | ✅ PASS | JWT-scoped; admin endpoint restricted |

---

## Test 1 — Unauthenticated Access

**Objective:** Confirm all protected routes reject requests with no token.

```bash
# Cart (must be authenticated)
curl -s -o /dev/null -w "Cart: %{http_code}\n" http://localhost:8000/gateway/cart

# Orders
curl -s -o /dev/null -w "Orders: %{http_code}\n" http://localhost:8000/gateway/orders/me

# Payments
curl -s -o /dev/null -w "Payments: %{http_code}\n" http://localhost:8000/gateway/payments/me

# Notifications
curl -s -o /dev/null -w "Notifications: %{http_code}\n" http://localhost:8000/gateway/notifications
```

**Expected:** All return `401`.  
**Note:** `GET /gateway/products` is intentionally public (no auth required) — 200 is correct.

---

## Test 2 — JWT Tampering / alg:none Attack

**Objective:** Confirm tampered and unsigned tokens are rejected.

```bash
# Tampered token (change one character in the payload)
TAMPERED="eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.TAMPERED_PAYLOAD.signature"
curl -s -o /dev/null -w "Tampered JWT: %{http_code}\n" \
  http://localhost:8000/gateway/cart \
  -H "Authorization: Bearer $TAMPERED"

# alg:none attack — base64url-encode {"alg":"none","typ":"JWT"} header
ALG_NONE_TOKEN="eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJoYWNrZXIiLCJyZWFsbV9hY2Nlc3MiOnsicm9sZXMiOlsiYWRtaW4iXX19."
curl -s -o /dev/null -w "alg:none token: %{http_code}\n" \
  http://localhost:8000/gateway/cart \
  -H "Authorization: Bearer $ALG_NONE_TOKEN"
```

**Expected:** Both return `401`.

---

## Test 3 — Privilege Escalation

**Objective:** Confirm regular users cannot access admin-only endpoints.

```bash
# Order status update (admin only)
ORDER_ID="00000000-0000-0000-0000-000000000001"
curl -s -o /dev/null -w "PUT order status (user): %{http_code}\n" \
  -X PUT "http://localhost:8000/gateway/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"status":"Confirmed"}'

# Admin user list (UserIdentity)
curl -s -o /dev/null -w "Admin users list (user): %{http_code}\n" \
  http://localhost:8000/gateway/identity/admin/users \
  -H "Authorization: Bearer $USER1_TOKEN"

# Admin notifications view
curl -s -o /dev/null -w "Admin notifications (user): %{http_code}\n" \
  http://localhost:8000/gateway/notifications/admin \
  -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** All return `403`.

---

## Test 4 — IDOR: Orders

**Objective:** Confirm users cannot read or cancel another user's orders.

```bash
# Create an order as user1 first, then try to access it as user2
# Step 1: Create order as user1 (requires items in cart)
# Step 2: Note the order ID from the response

# Cross-user read
ORDER_ID="<order-id-belonging-to-user1>"
curl -s -o /dev/null -w "User2 reads User1 order: %{http_code}\n" \
  "http://localhost:8000/gateway/orders/$ORDER_ID" \
  -H "Authorization: Bearer $USER2_TOKEN"

# Cross-user cancel
curl -s -o /dev/null -w "User2 cancels User1 order: %{http_code}\n" \
  -X DELETE "http://localhost:8000/gateway/orders/$ORDER_ID" \
  -H "Authorization: Bearer $USER2_TOKEN"

# Own orders (should work)
curl -s -o /dev/null -w "User1 reads own orders: %{http_code}\n" \
  "http://localhost:8000/gateway/orders/me" \
  -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** Cross-user → `403`. Own order → `200`.

---

## Test 4b — IDOR: Payments

**Objective:** Confirm users cannot read another user's payment history.

```bash
# Own payment history
curl -s -o /dev/null -w "User1 own payments: %{http_code}\n" \
  http://localhost:8000/gateway/payments/me \
  -H "Authorization: Bearer $USER1_TOKEN"

# Try to access user1's payments as user2 (no userId param accepted by design)
curl -s -o /dev/null -w "User2 queries payments/me: %{http_code}\n" \
  http://localhost:8000/gateway/payments/me \
  -H "Authorization: Bearer $USER2_TOKEN"
# Returns user2's own empty list — cannot cross-reference user1
```

**Expected:** Each user sees only their own data. No way to specify another user's ID.

---

## Test 5 — Body Injection (Registration)

**Objective:** Confirm extra/dangerous fields in register body are ignored.

```bash
curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username":"injtest_user",
    "email":"injtest@example.com",
    "password":"Test@1234",
    "firstName":"Inj",
    "lastName":"Test",
    "role":"admin",
    "isAdmin":true,
    "verified":true,
    "groups":["admin","superuser"]
  }'
```

**Expected:** `{"message":"User registered successfully."}` — extra fields silently ignored. Verify in Keycloak Admin that the registered user has only the `user` role.

---

## Test 6 — Cart userId Spoofing

**Objective:** Confirm cart operations use JWT identity, not a client-supplied userId.

```bash
# Attempt to add to cart with a spoofed userId in the body
curl -s -X POST http://localhost:8000/gateway/cart/items \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productId":"SPOOFED-PRODUCT-ID",
    "quantity":1,
    "userId":"victim-user-id",
    "price":99.99,
    "productName":"Test"
  }' -w "\nHTTP:%{http_code}"

# Confirm user1 can only see their own cart
curl -s http://localhost:8000/gateway/cart \
  -H "Authorization: Bearer $USER1_TOKEN" -w "\nHTTP:%{http_code}"
```

**Expected:** The `userId` field in the body is ignored; cart is created for the token holder.

---

## Test 7 — Input Validation

**Objective:** Confirm validation rejects malformed and boundary-violating inputs.

```bash
# Negative payment amount
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260101-AAAAAAAA","amount":-500,"currency":"INR"}' \
  -w "\nHTTP:%{http_code}"

# Zero amount
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260101-AAAAAAAA","amount":0,"currency":"INR"}' \
  -w "\nHTTP:%{http_code}"

# Missing required fields
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' -w "\nHTTP:%{http_code}"
```

**Expected:** All return `400` with validation error details.

---

## Test 8 — Information Disclosure

**Objective:** Confirm error responses do not leak stack traces or internal details.

```bash
# Non-existent resource
curl -s "http://localhost:8000/gateway/orders/00000000-0000-0000-0000-000000000001" \
  -H "Authorization: Bearer $USER1_TOKEN"

# Invalid GUID (triggers 400 not 500)
curl -s "http://localhost:8000/gateway/orders/not-a-guid" \
  -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** Clean JSON error message. No exception type names, file paths, line numbers, or stack traces.

---

## Test 9 — Gateway Rate Limiting

**Objective:** Confirm rate limiting enforces per-route request limits.

```bash
# Products route: limit is 20 req/1s
# IMPORTANT: requests must be truly concurrent — a sequential loop is too slow to
# saturate a 1-second window. Use background jobs to fire all requests at once.
tmpdir=$(mktemp -d)
for i in $(seq 1 30); do
  curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/gateway/products \
    -H "Authorization: Bearer $USER1_TOKEN" > "$tmpdir/$i.out" &
done
wait

SUCCESS=0; RATE_LIMITED=0
for f in "$tmpdir"/*.out; do
  code=$(cat "$f")
  [ "$code" = "200" ] && SUCCESS=$((SUCCESS+1))
  [ "$code" = "429" ] && RATE_LIMITED=$((RATE_LIMITED+1))
done
echo "200: $SUCCESS | 429: $RATE_LIMITED"
rm -rf "$tmpdir"
# Expect: 200: 20 | 429: 10
```

**Expected:** Exactly 20 requests pass; the remaining 10 return `429 Too Many Requests` with body `"Rate limit exceeded. Please slow down."`.

**Route limits configured in `ocelot.json`:**
| Route | Limit |
|-------|-------|
| `/gateway/products` | 20 req/1s |
| `/gateway/cart` | 10 req/1s |
| `/gateway/orders` | 10 req/1s |
| `/gateway/identity` | 30 req/1s |
| `/gateway/payments` | 30 req/1s |
| `/gateway/notifications` | 20 req/1s |

---

## Test 10 — HTTP Verb Tampering

**Objective:** Confirm routes reject unexpected HTTP methods.

```bash
# DELETE on a read-only collection route
curl -s -o /dev/null -w "DELETE /gateway/products: %{http_code}\n" \
  -X DELETE http://localhost:8000/gateway/products

# PATCH on orders (not a supported verb in the API)
curl -s -o /dev/null -w "PATCH /gateway/orders/me: %{http_code}\n" \
  -X PATCH http://localhost:8000/gateway/orders/me \
  -H "Authorization: Bearer $USER1_TOKEN"

# PUT on cart root
curl -s -o /dev/null -w "PUT /gateway/cart: %{http_code}\n" \
  -X PUT http://localhost:8000/gateway/cart \
  -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** `405 Method Not Allowed`.

---

## Test 11 — Brute Force Login Protection

**Objective:** Confirm Keycloak locks an account after repeated failed login attempts.

```bash
# 6 consecutive failed logins (threshold is 5)
for i in $(seq 1 6); do
  CODE=$(curl -s -o /dev/null -w "%{http_code}" \
    -X POST http://localhost:5085/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"sectest_user","password":"WrongPassword!"}')
  echo "Attempt $i: $CODE"
done

# 7th attempt — account should now be locked
curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}'
```

**Expected:** After 5 failures, the 6th attempt (even with the correct password) returns an error indicating the account is temporarily locked.

**Keycloak brute force settings (configured in `keycloak/antkart-realm.json`):**
| Setting | Value |
|---------|-------|
| `bruteForceProtected` | `true` |
| `failureFactor` | `5` |
| `waitIncrementSeconds` | `60` |
| `maxFailureWaitSeconds` | `900` (15 min) |
| `permanentLockout` | `false` |

To unlock a user manually via Keycloak Admin Portal (`http://localhost:8090`) → Users → select user → Credentials tab → Clear temporary lockout.

---

## Test 12 — Response Header Exposure

**Objective:** Confirm the `Server` header is suppressed to avoid technology fingerprinting.

```bash
# Check Gateway response headers
curl -sI http://localhost:8000/gateway/products | grep -i "server\|x-powered\|x-aspnet"

# Check a direct service (should also be suppressed)
curl -sI http://localhost:5087/health | grep -i "server"
```

**Expected:** No `Server:` header in any response. All services set `AddServerHeader = false` in Kestrel options.

---

## Test 13 — Direct Service Bypass (Skip Gateway)

**Objective:** Confirm auth is enforced at the service layer, not just the gateway.

```bash
# Products: intentionally public — 200 expected
curl -s -o /dev/null -w "Direct Products (no auth): %{http_code}\n" \
  http://localhost:8080/api/v1/products

# Orders: requires auth — 401 expected even without gateway
curl -s -o /dev/null -w "Direct Orders (no auth): %{http_code}\n" \
  http://localhost:8083/api/orders/me

# Cart: requires auth — 401 expected
curl -s -o /dev/null -w "Direct Cart (no auth): %{http_code}\n" \
  http://localhost:8082/api/v1/cart

# Payments: requires auth — 401 expected
curl -s -o /dev/null -w "Direct Payments (no auth): %{http_code}\n" \
  http://localhost:8085/api/payments/me
```

**Expected:** Auth-protected services return `401` even when the gateway is bypassed. Defence in depth is working.

---

## Test 14 — Mass Assignment (Payments)

**Objective:** Confirm extra fields injected in the request body are silently ignored and do not affect the stored record.

```bash
# Inject userId, isAdmin, role into a payment initiation request
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "orderId":"00000000-0000-0000-0000-000000000001",
    "orderNumber":"ORD-20260101-AAAAAAAA",
    "amount":100.00,
    "currency":"INR",
    "userId":"hacker-injected-id",
    "isAdmin":true,
    "role":"admin"
  }' -w "\nHTTP:%{http_code}"
```

**Expected:** `200` with a valid payment record. The `userId` stored in the database is the token holder's `sub` claim — not `"hacker-injected-id"`. Verify by calling `GET /gateway/payments/me` with both user tokens.

**Why it's safe:** The `InitiatePaymentRequest` endpoint-layer DTO has no `userId` field. Extra JSON fields are silently dropped by `System.Text.Json`. The handler reads `userId` exclusively from `http.GetUserId()`.

---

## Test 15 — Notification IDOR

**Objective:** Confirm users can only read their own notifications.

```bash
# User1 sees their own notifications
curl -s http://localhost:8000/gateway/notifications \
  -H "Authorization: Bearer $USER1_TOKEN"

# User2 tries to access admin notification view (all users)
curl -s -o /dev/null -w "User2 → admin notifications: %{http_code}\n" \
  http://localhost:8000/gateway/notifications/admin \
  -H "Authorization: Bearer $USER2_TOKEN"

# User2 tries to read a specific notification belonging to User1
# (replace NOTIF_ID with a real ID from User1's notification list)
NOTIF_ID="<notification-id-from-user1>"
curl -s -o /dev/null -w "User2 reads User1 notification: %{http_code}\n" \
  "http://localhost:8000/gateway/notifications/$NOTIF_ID" \
  -H "Authorization: Bearer $USER2_TOKEN"
```

**Expected:**
- `GET /gateway/notifications` — each user sees only their own (JWT-scoped in handler)
- `GET /gateway/notifications/admin` with user token → `403`
- `GET /gateway/notifications/{id}` for another user's notification → `403`

---

## Vulnerability History

| ID | Severity | Description | Status | Fixed In |
|----|----------|-------------|--------|----------|
| VULN-001 | Critical | Gateway rate limiting silently disabled — `AddMemoryCache()` missing | Fixed | `AK.Gateway/Program.cs` |
| VULN-002 | Medium | Keycloak brute force threshold too high (30 → 5 attempts) | Fixed | `keycloak/antkart-realm.json` |
| VULN-003 | Critical | IDOR — userId accepted as URL path param, not validated against JWT | Fixed | Cart, Order, Payments, Notification endpoints |
| INFO-001 | Low | `Server: Kestrel` header exposed on all responses | Fixed | All service `Program.cs` files |

---

## Re-running All Tests

A convenience script is available at the repo root:

```bash
bash test_all.sh
```

This script covers functional flow testing. For security-specific regression testing, re-run the curl commands in this document against the live stack after every change to auth, routing, or endpoint logic.
