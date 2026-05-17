# Skill: Run Security Checks

**Purpose:** Execute the full 15-category ethical security test suite from `SECURITY_TESTS.md` against the live Docker stack. Reports each test as PASS/FAIL/WARN and flags any regressions from the last known-good baseline.

---

## When to Use
- Before any release or deployment to a shared environment
- After adding new endpoints, changing auth policies, or modifying middleware
- After changes to `ocelot.json` (routing or rate limits)
- After any change to Keycloak realm settings
- Periodically as a scheduled security health check

## Prerequisites
- Docker Compose stack fully up: `docker-compose up -d`
- All 16 containers healthy: `docker ps` shows `Up` or `healthy` for all
- Two test accounts registered: `sectest_user` and `sectest_user2` (see Setup in `SECURITY_TESTS.md`)

---

## Setup

```bash
# Confirm stack is up
docker ps --format "table {{.Names}}\t{{.Status}}" | grep -v NAMES

# Register test accounts (skip if already registered)
curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","email":"sectest@example.com","password":"SecTest@123","firstName":"Sec","lastName":"Test"}'

curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user2","email":"sectest2@example.com","password":"SecTest@123","firstName":"Sec2","lastName":"Test2"}'

# Fetch fresh tokens (tokens expire — always re-fetch at start of session)
USER1_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

USER2_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user2","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

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

# ── Test 5: Body injection ───────────────────────────────────────────────────
echo "── Test 5: Body injection (register)"
REG=$(curl -s -X POST http://localhost:5085/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"injtest99","email":"injtest99@example.com","password":"Test@1234","firstName":"Inj","lastName":"Test","role":"admin","isAdmin":true}')
echo "$REG" | grep -qi "success" && echo "✅ PASS | Body injection — extra fields ignored" && PASS=$((PASS+1)) \
  || { echo "❌ FAIL | Body injection — unexpected response: $REG"; FAIL=$((FAIL+1)); }

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

# ── Test 11: Brute force protection ──────────────────────────────────────────
echo "── Test 11: Brute force login protection"
for i in $(seq 1 5); do
  curl -s -o/dev/null -X POST http://localhost:5085/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"username":"sectest_user","password":"WrongPassword!!"}'
done
LOCKED=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}')
echo "$LOCKED" | grep -qiE "lock|invalid_grant|temporar" \
  && { echo "✅ PASS | Account locked after 5 failures"; PASS=$((PASS+1)); } \
  || { echo "⚠️  WARN | Account may not be locked (check Keycloak failureFactor)"; WARN=$((WARN+1)); }

# Unlock test user
KC_ADMIN_TOKEN=$(curl -s -X POST "http://localhost:8090/realms/master/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=admin-cli&username=admin&password=admin" \
  | python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token',''))")
USER_KC_ID=$(curl -s "http://localhost:8090/admin/realms/antkart/users?username=sectest_user" \
  -H "Authorization: Bearer $KC_ADMIN_TOKEN" \
  | python3 -c "import sys,json; u=json.load(sys.stdin); print(u[0]['id'] if u else '')")
[ -n "$USER_KC_ID" ] && curl -s -X DELETE \
  "http://localhost:8090/admin/realms/antkart/attack-detection/brute-force/users/$USER_KC_ID" \
  -H "Authorization: Bearer $KC_ADMIN_TOKEN" > /dev/null

# Refresh token after unlock
USER1_TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))")

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
[ "$FAIL" -gt 0 ] && echo "ACTION REQUIRED: $FAIL test(s) failed. See SECURITY_TESTS.md for fix guidance." \
  || echo "All tests passed. Review any warnings above."
echo "══════════════════════════════════════"
```

---

## Baseline (All-Green State)

After all current fixes are applied, the expected baseline is:

| Test | Expected |
|------|----------|
| 1–3, 5–10, 12–15 | PASS |
| 11 (Brute force) | PASS (failureFactor=5) |
| FAIL count | 0 |
| WARN count | 0 |

If any test drops from PASS to FAIL after a code change, treat it as a regression and do not merge until fixed.

---

## After Test 11 — Re-enable Test User

Test 11 locks `sectest_user`. The script above auto-unlocks it via Keycloak Admin API. If the script was interrupted, unlock manually:

```bash
# In Keycloak Admin Portal: http://localhost:8090
# Realm: antkart → Users → sectest_user → Sessions → Clear sessions / credentials
```
