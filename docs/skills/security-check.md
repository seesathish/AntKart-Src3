# Skill: Run Security Checks

**Purpose:** Execute the full 15-category ethical security test suite from `docs/test/SECURITY_TESTS.md` against the running platform. Reports each test as PASS/FAIL/WARN and flags any regressions from the last known-good baseline.

> **Identity is Microsoft Entra ID.** There is no application `/api/auth/register` or `/api/auth/login` endpoint — Entra issues tokens directly through standard OAuth flows. Acquire test tokens for the API (`api://antkart-api-dev`) via the OAuth2 Authorization Code + PKCE flow (see [oauth2-pkce-concepts](../guides/oauth2-pkce-concepts.md)); user and app-role administration is done in Entra / Microsoft Graph, and account lockout is enforced by **Entra Smart Lockout**, not an application setting.

---

## When to Use
- Before any release or deployment to a shared environment
- After adding new endpoints, changing auth policies, or modifying middleware
- After changes to `ocelot.json` (routing or rate limits)
- After any change to Entra ID app registration settings (app roles, optional claims, exposed scopes)
- Periodically as a scheduled security health check

## Prerequisites
- The AntKart services running and reachable — cloud-deployed, or run locally against live cloud services / via cloud port-forwarding
- All services healthy (`GET /health/ready` on each, or `GET /gateway/health/*` through the gateway)
- Two Entra ID test users, `USER1` and `USER2`, both **without** the `admin` app role (the privilege-escalation tests confirm non-admins are rejected). Provision them and assign app roles in Entra / Microsoft Graph.

---

## Setup

Acquire an access token for each test user through the Entra OAuth2 Authorization Code + PKCE flow (see [oauth2-pkce-concepts](../guides/oauth2-pkce-concepts.md)), then export them. Tokens expire, so re-acquire at the start of each session.

```bash
# Confirm the services are reachable (through the gateway).
curl -s -o/dev/null -w "gateway: %{http_code}\n" http://localhost:8000/gateway/health/products

# Paste tokens obtained for each test user via the Entra Authorization Code + PKCE flow.
# Each token's audience must be api://antkart-api-dev. Both users are standard (non-admin).
USER1_TOKEN="<access token for test user 1>"
USER2_TOKEN="<access token for test user 2>"

echo "Tokens ready. USER1: ${USER1_TOKEN:0:30}..."
```

---

## Run All Tests

Copy-paste the block below. Results are printed to stdout. Each test is self-contained.

```bash
#!/usr/bin/env bash
set -euo pipefail

PASS=0; FAIL=0; WARN=0

check() {
  local label=$1 actual=$2 expected=$3
  if [ "$actual" = "$expected" ]; then
    echo "✅ PASS | $label (got $actual)"
    PASS=$((PASS+1))
  else
    echo "❌ FAIL | $label (expected $expected, got $actual)"
    FAIL=$((FAIL+1))
  fi
}

warn() {
  local label=$1 detail=$2
  echo "⚠️  WARN | $label — $detail"
  WARN=$((WARN+1))
}

echo "=== AntKart Security Test Suite ==="
echo ""

# ── Test 1: Unauthenticated access ──────────────────────────────────────────
echo "── Test 1: Unauthenticated access"
check "Cart (no token)"    "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/cart)"          "401"
check "Orders/me (no token)" "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/orders/me)"  "401"
check "Payments/me (no token)" "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/payments/me)" "401"
check "Notifications (no token)" "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/notifications)" "401"

# ── Test 2: JWT tampering ────────────────────────────────────────────────────
echo "── Test 2: JWT tampering / alg:none"
TAMPERED="eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.TAMPERED.signature"
ALG_NONE="eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJoYWNrZXIifQ."
check "Tampered JWT"  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/cart -H "Authorization: Bearer $TAMPERED")" "401"
check "alg:none JWT"  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/cart -H "Authorization: Bearer $ALG_NONE")" "401"

# ── Test 3: Privilege escalation ─────────────────────────────────────────────
echo "── Test 3: Privilege escalation"
check "Admin users (user token)"         "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/identity/admin/users -H "Authorization: Bearer $USER1_TOKEN")" "403"
check "Admin notifications (user token)" "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/notifications/admin -H "Authorization: Bearer $USER1_TOKEN")" "403"

# ── Test 4: IDOR — Orders ────────────────────────────────────────────────────
echo "── Test 4: IDOR — Orders"
ORDER_LIST=$(curl -s http://localhost:8000/gateway/orders/me -H "Authorization: Bearer $USER1_TOKEN")
ORDER_ID=$(echo "$ORDER_LIST" | python3 -c "import sys,json; items=json.load(sys.stdin).get('items',[]); print(items[0]['id'] if items else 'NONE')" 2>/dev/null)
if [ "$ORDER_ID" != "NONE" ] && [ -n "$ORDER_ID" ]; then
  check "User2 reads User1 order" \
    "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/orders/$ORDER_ID -H "Authorization: Bearer $USER2_TOKEN")" "403"
else
  warn "IDOR-Order" "No existing orders for User1 — create one and re-run"
fi

# ── Test 5: Body injection (registration) — N/A under Entra ──────────────────
echo "── Test 5: Body injection (registration)"
warn "Body injection (register)" "N/A — there is no application registration endpoint under Entra ID; user provisioning and role assignment are done in Entra / Microsoft Graph. Mass-assignment / extra-field injection is still covered by Test 6 (cart) and Test 14 (payments)."

# ── Test 6: Cart userId spoofing ─────────────────────────────────────────────
echo "── Test 6: Cart userId spoofing"
CODE=$(curl -s -o/dev/null -w "%{http_code}" -X POST http://localhost:8000/gateway/cart/items \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productId":"00000000-0000-0000-0000-000000000001","quantity":1,"userId":"hacker-id","price":99.99,"productName":"Test"}')
[ "$CODE" != "500" ] && echo "✅ PASS | Cart userId spoofing ($CODE — field ignored or validation failed)" && PASS=$((PASS+1)) \
  || { echo "❌ FAIL | Cart userId spoofing — 500 unexpected"; FAIL=$((FAIL+1)); }

# ── Test 7: Input validation ─────────────────────────────────────────────────
echo "── Test 7: Input validation"
check "Negative payment amount" \
  "$(curl -s -o/dev/null -w "%{http_code}" -X POST http://localhost:8000/gateway/payments/initiate \
    -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
    -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260517-AAAA","amount":-100,"currency":"INR"}')" "400"
check "Zero payment amount" \
  "$(curl -s -o/dev/null -w "%{http_code}" -X POST http://localhost:8000/gateway/payments/initiate \
    -H "Authorization: Bearer $USER1_TOKEN" -H "Content-Type: application/json" \
    -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260517-AAAA","amount":0,"currency":"INR"}')" "400"

# ── Test 8: Information disclosure ───────────────────────────────────────────
echo "── Test 8: Information disclosure"
RESP=$(curl -s http://localhost:8000/gateway/orders/00000000-0000-0000-0000-000000000001 \
  -H "Authorization: Bearer $USER1_TOKEN")
echo "$RESP" | grep -qiE "Exception|StackTrace|at .* in .*\.cs" \
  && { echo "❌ FAIL | Stack trace exposed: $RESP"; FAIL=$((FAIL+1)); } \
  || { echo "✅ PASS | No stack trace in error response"; PASS=$((PASS+1)); }

# ── Test 9: Rate limiting ─────────────────────────────────────────────────────
echo "── Test 9: Gateway rate limiting"
tmpdir=$(mktemp -d)
for i in $(seq 1 30); do
  curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/products \
    -H "Authorization: Bearer $USER1_TOKEN" > "$tmpdir/$i.out" &
done; wait
RL=$(grep -l "429" "$tmpdir"/*.out 2>/dev/null | wc -l)
rm -rf "$tmpdir"
[ "$RL" -gt 0 ] \
  && { echo "✅ PASS | Rate limiting enforced ($RL/30 rate-limited)"; PASS=$((PASS+1)); } \
  || { echo "❌ FAIL | Rate limiting NOT enforcing (0 of 30 returned 429)"; FAIL=$((FAIL+1)); }

# ── Test 10: HTTP verb tampering ──────────────────────────────────────────────
echo "── Test 10: HTTP verb tampering"
check "DELETE /gateway/products" \
  "$(curl -s -o/dev/null -w "%{http_code}" -X DELETE http://localhost:8000/gateway/products -H "Authorization: Bearer $USER1_TOKEN")" "405"

# ── Test 11: Brute force protection — verified in Entra, not the application ──
echo "── Test 11: Brute force / account-lockout protection"
warn "Brute force protection" "Handled by Entra ID Smart Lockout at the identity provider — sign-in and password validation never reach the application, so there is no application login endpoint to script against. Verify Smart Lockout is enabled and review the lockout events in the Entra sign-in logs (Microsoft Entra admin center → Monitoring → Sign-in logs). No application-side unlock is required; Smart Lockout resets automatically."

# ── Test 12: Server header ────────────────────────────────────────────────────
echo "── Test 12: Server header exposure"
SERVER_HEADER=$(curl -sI http://localhost:8000/gateway/products | grep -i "^server:")
[ -z "$SERVER_HEADER" ] \
  && { echo "✅ PASS | No Server header"; PASS=$((PASS+1)); } \
  || { echo "❌ FAIL | Server header exposed: $SERVER_HEADER"; FAIL=$((FAIL+1)); }

# ── Test 13: Direct service bypass ───────────────────────────────────────────
echo "── Test 13: Direct service bypass"
check "Direct Orders (no auth)" \
  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8083/api/orders/me)" "401"
check "Direct Cart (no auth)" \
  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8082/api/v1/cart)" "401"
check "Direct Payments (no auth)" \
  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8085/api/payments/me)" "401"

# ── Test 14: Mass assignment ──────────────────────────────────────────────────
echo "── Test 14: Mass assignment (Payments)"
RESP=$(curl -s -X POST http://localhost:8000/gateway/payments/initiate \
  -H "Authorization: Bearer $USER1_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","orderNumber":"ORD-20260517-AAAA","amount":100,"currency":"INR","userId":"hacker-id","isAdmin":true}')
echo "$RESP" | grep -qi "hacker-id" \
  && { echo "❌ FAIL | Mass assignment — injected userId reflected in response"; FAIL=$((FAIL+1)); } \
  || { echo "✅ PASS | Mass assignment — extra fields ignored"; PASS=$((PASS+1)); }

# ── Test 15: Notification IDOR ────────────────────────────────────────────────
echo "── Test 15: Notification IDOR"
check "User2 → admin notifications" \
  "$(curl -s -o/dev/null -w "%{http_code}" http://localhost:8000/gateway/notifications/admin -H "Authorization: Bearer $USER2_TOKEN")" "403"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "══════════════════════════════════════"
echo "PASS: $PASS | FAIL: $FAIL | WARN: $WARN"
[ "$FAIL" -gt 0 ] && echo "ACTION REQUIRED: $FAIL test(s) failed. See docs/test/SECURITY_TESTS.md for fix guidance." \
  || echo "All tests passed. Review any warnings above."
echo "══════════════════════════════════════"
```

---

## Baseline (All-Green State)

After all current fixes are applied, the expected baseline is:

| Test | Expected |
|------|----------|
| 1–4, 6–10, 12–15 | PASS |
| 5 (Body injection / register) | WARN (N/A — no application registration endpoint under Entra) |
| 11 (Brute force) | WARN (verified via Entra Smart Lockout / sign-in logs) |
| FAIL count | 0 |
| WARN count | 2 (Tests 5 and 11 are informational under Entra) |

If any test drops from PASS to FAIL after a code change, treat it as a regression and do not merge until fixed.

---

## Account lockout under Entra

Account-lockout and brute-force protection are enforced by **Entra ID Smart Lockout** at the identity provider — the application has no login endpoint to lock or unlock, so there is nothing to re-enable after a run. Confirm Smart Lockout configuration and review lockout events in the Microsoft Entra admin center (Monitoring → Sign-in logs).
