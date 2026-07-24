# AntKart — Security Test Guide

This document covers ethical black-box and grey-box security tests for the **current** AntKart platform: Microsoft **Entra ID** authentication, Azure **Service Bus** messaging, serverless **Azure Communication Services** notifications, and the real `/gateway/*` routes. All tests run against the live, running services.

> **Note (2026-07-24): Entra-native, no application identity service.** The standalone identity service was retired (see [ADR-021](../adr/ADR-021-retire-identity-service-for-entra.md)) — there is **no** `/api/auth/register`/`/api/auth/login` endpoint and **no** `/gateway/identity*` route. Tokens are obtained directly from Entra (see Setup). Notifications are **serverless** (Event Grid → Functions → ACS) with **no client-facing HTTP surface**, so there is no `/gateway/notifications*` route to test. Tests that targeted those retired components are updated or marked **N/A** below.

> **PowerShell note.** These are bash examples (correct as-is for bash / WSL / Git Bash). In native **PowerShell**, `curl` is an alias for `Invoke-WebRequest`, so use **`curl.exe`** for the `-s`/`-H`/`-k`/`-w` flags. See [Operations Command Reference → Gotchas](../guides/operations-command-reference.md#j-gotchas-and-powershell-notes).

**Prerequisites:** the AntKart services running and reachable — cloud-deployed (through the ingress) or run locally against live cloud services — plus `curl` (or `curl.exe`) and Python 3.

---

## Setup — acquire two test-user tokens (Entra)

There is no application login endpoint; tokens come from Entra. Acquire an access token for the API (`api://antkart-api-dev`) for **two distinct Entra test users** via the OAuth2 Authorization Code + PKCE flow (see [OAuth2 + PKCE Concepts](../guides/oauth2-pkce-concepts.md)). Both users must be **non-admin** (they hold no `admin` app role) — the privilege-escalation tests confirm non-admins are rejected. Provision the users and assign app roles in Entra / Microsoft Graph.

```bash
# Paste tokens obtained for each test user via the Entra PKCE flow.
# Each token's audience must be api://antkart-api-dev. Both users are standard (non-admin).
USER1_TOKEN="<access token for test user 1>"
USER2_TOKEN="<access token for test user 2>"
```

The gateway base URL is `http://localhost:8000` for a local run, or `https://<public-ip>.nip.io` through the cluster ingress (add `curl.exe -k` for the Let's Encrypt **staging** certificate). Substitute `<GATEWAY>` below.

---

## Test Results Summary

| # | Category | Expected |
|---|----------|----------|
| 1 | Unauthenticated access | 401 on all protected routes |
| 2 | JWT tampering / alg:none | 401 on forged tokens |
| 3 | Privilege escalation | 403 for a regular user on admin routes |
| 4 | IDOR — Orders | 403 cross-user order access |
| 4b | IDOR — Payments | Each user sees only their own data |
| 5 | Body injection (registration) | **N/A under Entra** — no application registration endpoint |
| 6 | Cart userId spoofing | JWT `sub` used; injected field ignored |
| 7 | Input validation | 400 on invalid amounts |
| 8 | Information disclosure | No stack traces in error responses |
| 9 | Gateway rate limiting | 429 after the per-route threshold |
| 10 | HTTP verb tampering | 405 on wrong verbs |
| 11 | Brute force / account lockout | Enforced by **Entra Smart Lockout** (verified in Entra sign-in logs) |
| 12 | Server header exposure | No `Server` header in responses |
| 13 | Direct service bypass | Auth enforced on each service directly |
| 14 | Mass assignment (Payments) | Extra body fields ignored |
| 15 | Notification IDOR | **N/A** — notifications have no client-facing HTTP surface |

---

## Test 1 — Unauthenticated Access

**Objective:** confirm protected routes reject requests with no token.

```bash
curl -s -o /dev/null -w "Cart: %{http_code}\n"     http://localhost:8000/gateway/cart
curl -s -o /dev/null -w "Orders: %{http_code}\n"   http://localhost:8000/gateway/orders/me
curl -s -o /dev/null -w "Payments: %{http_code}\n" http://localhost:8000/gateway/payments/me
```

**Expected:** all return `401`.
**Note:** `GET /gateway/products` is intentionally public — `200` is correct.

---

## Test 2 — JWT Tampering / alg:none Attack

**Objective:** confirm tampered and unsigned tokens are rejected.

```bash
TAMPERED="eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.TAMPERED_PAYLOAD.signature"
curl -s -o /dev/null -w "Tampered JWT: %{http_code}\n" \
  http://localhost:8000/gateway/cart -H "Authorization: Bearer $TAMPERED"

# alg:none — base64url of {"alg":"none",...} with a flat roles:[admin] claim
ALG_NONE_TOKEN="eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJoYWNrZXIiLCJyb2xlcyI6WyJhZG1pbiJdfQ."
curl -s -o /dev/null -w "alg:none token: %{http_code}\n" \
  http://localhost:8000/gateway/cart -H "Authorization: Bearer $ALG_NONE_TOKEN"
```

**Expected:** both return `401` — Entra JWT validation rejects an unsigned/tampered token at the gateway and again at each service (defence in depth).

---

## Test 3 — Privilege Escalation

**Objective:** confirm a regular user cannot reach admin-only endpoints. The current admin-gated routes are the **order status update** and **product writes**.

```bash
# Order status update — admin only
ORDER_ID="00000000-0000-0000-0000-000000000001"
curl -s -o /dev/null -w "PUT order status (user): %{http_code}\n" \
  -X PUT "http://localhost:8000/gateway/orders/$ORDER_ID/status" \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"status":"Confirmed"}'

# Product create — admin only
curl -s -o /dev/null -w "POST product (user): %{http_code}\n" \
  -X POST "http://localhost:8000/gateway/products" \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"x","categoryName":"Men","subCategoryName":"Shirts","price":10}'
```

**Expected:** both return `403` — authorization reads the flat `roles` claim; a token without `admin` is rejected.

---

## Test 4 — IDOR: Orders

**Objective:** confirm users cannot read or cancel another user's orders. (Create an order as user1 first — see the [Developer Test Guide](DevTestGuide.md) — and note its id.)

```bash
ORDER_ID="<order-id-belonging-to-user1>"
curl -s -o /dev/null -w "User2 reads User1 order: %{http_code}\n" \
  "http://localhost:8000/gateway/orders/$ORDER_ID" -H "Authorization: Bearer $USER2_TOKEN"
curl -s -o /dev/null -w "User2 cancels User1 order: %{http_code}\n" \
  -X DELETE "http://localhost:8000/gateway/orders/$ORDER_ID" -H "Authorization: Bearer $USER2_TOKEN"
curl -s -o /dev/null -w "User1 reads own orders: %{http_code}\n" \
  "http://localhost:8000/gateway/orders/me" -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** cross-user → `403`; own → `200`. Ownership is checked against `http.GetUserId()` (the JWT `sub`), never a path/body value.

---

## Test 4b — IDOR: Payments

**Objective:** confirm users cannot read another user's payment history — the API exposes only `/me`, never another user's id.

```bash
curl -s -o /dev/null -w "User1 own payments: %{http_code}\n" \
  http://localhost:8000/gateway/payments/me -H "Authorization: Bearer $USER1_TOKEN"
curl -s -o /dev/null -w "User2 payments/me: %{http_code}\n" \
  http://localhost:8000/gateway/payments/me -H "Authorization: Bearer $USER2_TOKEN"
```

**Expected:** each user sees only their own data; there is no parameter to specify another user's id.

---

## Test 5 — Body Injection (Registration) — N/A under Entra

**Not applicable.** There is no application registration endpoint under Entra ID — user provisioning and role assignment are done in **Entra / Microsoft Graph**, so a client cannot self-register or inject role/`isAdmin` fields at registration. Extra-field / mass-assignment injection **is** still exercised against the endpoints that accept request bodies — see **Test 6** (cart) and **Test 14** (payments).

---

## Test 6 — Cart userId Spoofing

**Objective:** confirm cart operations use the JWT identity, not a client-supplied `userId`.

```bash
curl -s -X POST http://localhost:8000/gateway/cart/items \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"productId":"SPOOFED-PRODUCT-ID","quantity":1,"userId":"victim-user-id","price":99.99,"productName":"Test"}' \
  -w "\nHTTP:%{http_code}"
curl -s http://localhost:8000/gateway/cart -H "Authorization: Bearer $USER1_TOKEN" -w "\nHTTP:%{http_code}"
```

**Expected:** the `userId` field is ignored; the cart belongs to the token holder (the endpoint DTO has no `userId`; extra JSON is dropped by `System.Text.Json`).

---

## Test 7 — Input Validation

**Objective:** confirm validation rejects malformed and boundary-violating inputs.

```bash
# Negative amount
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260101-AAAAAAAA","amount":-500,"currency":"INR"}' -w "\nHTTP:%{http_code}"
# Zero amount
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260101-AAAAAAAA","amount":0,"currency":"INR"}' -w "\nHTTP:%{http_code}"
# Missing fields
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" -d '{}' -w "\nHTTP:%{http_code}"
```

**Expected:** all return `400` with validation details (FluentValidation).

---

## Test 8 — Information Disclosure

**Objective:** confirm error responses do not leak stack traces or internals.

```bash
curl -s "http://localhost:8000/gateway/orders/00000000-0000-0000-0000-000000000001" -H "Authorization: Bearer $USER1_TOKEN"
curl -s "http://localhost:8000/gateway/orders/not-a-guid" -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** clean JSON error; no exception type names, file paths, line numbers, or stack traces (the shared `ExceptionHandlerMiddleware` maps exceptions to status codes).

---

## Test 9 — Gateway Rate Limiting

**Objective:** confirm per-route rate limiting. Requests must be truly concurrent — a sequential loop is too slow to saturate a 1-second window.

```bash
tmpdir=$(mktemp -d)
for i in $(seq 1 30); do
  curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/gateway/products \
    -H "Authorization: Bearer $USER1_TOKEN" > "$tmpdir/$i.out" &
done; wait
SUCCESS=0; RATE_LIMITED=0
for f in "$tmpdir"/*.out; do c=$(cat "$f"); [ "$c" = "200" ] && SUCCESS=$((SUCCESS+1)); [ "$c" = "429" ] && RATE_LIMITED=$((RATE_LIMITED+1)); done
echo "200: $SUCCESS | 429: $RATE_LIMITED"     # expect ~ 200: 20 | 429: 10
rm -rf "$tmpdir"
```

**Expected:** ~20 pass; the rest return `429` with body `"Rate limit exceeded. Please slow down."`.

**Per-route limits (from `ocelot.json`):**

| Route | Limit |
|-------|-------|
| `/gateway/products` | 20 req/1s |
| `/gateway/cart` | 10 req/1s |
| `/gateway/orders` | 10 req/1s |
| `/gateway/payments` | 30 req/1s |

(`AddMemoryCache()` in the gateway backs the rate-limit counters — without it, limits are silently ignored; see VULN-001.)

---

## Test 10 — HTTP Verb Tampering

**Objective:** confirm routes reject unexpected HTTP methods.

```bash
curl -s -o /dev/null -w "DELETE /gateway/products: %{http_code}\n" -X DELETE http://localhost:8000/gateway/products
curl -s -o /dev/null -w "PATCH /gateway/orders/me: %{http_code}\n" -X PATCH http://localhost:8000/gateway/orders/me -H "Authorization: Bearer $USER1_TOKEN"
```

**Expected:** the gateway routes only the configured verbs; an unconfigured verb is not routed (`404`/`405`).

---

## Test 11 — Brute Force / Account Lockout — enforced by Entra

**Enforced at the identity provider, not the application.** With Entra ID there is no application login endpoint to brute-force; password validation happens at Entra, which applies **Smart Lockout** automatically (locking the account after repeated failures, per the tenant's configuration). There is nothing in the application to script against.

**How to verify:** confirm Smart Lockout is enabled for the tenant and review lockout events in the **Microsoft Entra admin center → Monitoring → Sign-in logs**. No application-side unlock is required — Smart Lockout resets automatically.

---

## Test 12 — Response Header Exposure

**Objective:** confirm the `Server` header is suppressed (no technology fingerprinting).

```bash
curl -sI http://localhost:8000/gateway/products | grep -i "server\|x-powered\|x-aspnet"
curl -sI http://localhost:5077/health | grep -i "server"     # a service directly (Products dev port)
```

**Expected:** no `Server:` header — every service sets `AddServerHeader = false` in Kestrel.

---

## Test 13 — Direct Service Bypass (Skip Gateway)

**Objective:** confirm auth is enforced at the service layer, not only the gateway (defence in depth). Reach a service directly on its local dev port (or via `kubectl port-forward` in the cluster).

```bash
curl -s -o /dev/null -w "Direct Products (no auth): %{http_code}\n" http://localhost:5077/api/v1/products   # public -> 200
curl -s -o /dev/null -w "Direct Orders (no auth): %{http_code}\n"   http://localhost:5080/api/orders/me      # 401
curl -s -o /dev/null -w "Direct Cart (no auth): %{http_code}\n"     http://localhost:5079/api/v1/cart        # 401
curl -s -o /dev/null -w "Direct Payments (no auth): %{http_code}\n" http://localhost:5086/api/payments/me    # 401
```

**Expected:** auth-protected services return `401` even when the gateway is bypassed. (Local dev ports: Products 5077, Cart 5079, Order 5080, Payments 5086.)

---

## Test 14 — Mass Assignment (Payments)

**Objective:** confirm extra body fields are ignored and cannot influence the stored record.

```bash
curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260101-AAAAAAAA","amount":100.00,"currency":"INR","userId":"hacker-injected-id","isAdmin":true,"role":"admin"}' \
  -w "\nHTTP:%{http_code}"
```

**Expected:** the injected `userId`/`isAdmin`/`role` are ignored — the stored `userId` is the token holder's `sub` (verify with `GET /gateway/payments/me`). The `InitiatePaymentRequest` DTO has no `userId`; the handler reads it only from `http.GetUserId()`.

---

## Test 15 — Notification IDOR — N/A (serverless notifications)

**Not applicable.** Notifications are delivered by a serverless path (Order/Payments → Event Grid → Azure Functions → ACS email) with **no client-facing HTTP surface** and **no `/gateway/notifications*` route** — there is no notification API for a client to enumerate, so there is no notification IDOR to test. Notification **delivery** is verified end to end by placing an order and confirming the emails are received (see [Cluster End-to-End Verification](README.md#cluster-end-to-end-verification-public-ingress)); the recipient is derived from the signed-in user's `email` claim, never a client-supplied id.

---

## Vulnerability History

| ID | Severity | Description | Status |
|----|----------|-------------|--------|
| VULN-001 | Critical | Gateway rate limiting silently disabled — `AddMemoryCache()` missing | Fixed (`AK.Gateway/Program.cs`) |
| VULN-003 | Critical | IDOR — `userId` accepted as a URL path/body param, not validated against the JWT | Fixed (Cart, Order, Payments endpoints) |
| INFO-001 | Low | `Server: Kestrel` header exposed on all responses | Fixed (all services set `AddServerHeader = false`) |

*(A former item on the Phase-1 identity provider's brute-force threshold no longer applies — account lockout is now enforced by Entra Smart Lockout; see Test 11.)*

---

## Re-running the tests

Re-run the `curl` commands in this document against the live stack after any change to authentication, routing, or endpoint logic. For the automated regression suites (unit + integration), see the [Testing index](README.md); for the functional end-to-end walk-through, the [Developer Test Guide](DevTestGuide.md); for the cluster end-to-end path, [Cluster End-to-End Verification](README.md#cluster-end-to-end-verification-public-ingress).
