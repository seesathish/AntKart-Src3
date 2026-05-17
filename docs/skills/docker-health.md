# Skill: Docker Health Check

**Purpose:** Rebuild changed services, verify all 16 containers reach healthy/running status, smoke-test every Gateway route, and produce a final state report.

---

## When to Use
- After any code change before declaring "done"
- After adding a new service or changing `docker-compose.yml`
- When a container is crashing or showing `unhealthy`
- As a pre-release gate

---

## Step 1 — Identify What Needs Rebuilding

Only rebuild services whose code changed. Rebuilding everything takes ~10 minutes; rebuilding one service takes ~60 seconds.

```bash
# See which source files changed since last commit
git diff --name-only HEAD

# Map changed files to services:
# AK.Gateway/           → ak-gateway-api
# AK.Products/          → ak-products-api
# AK.Discount/          → ak-discount-grpc
# AK.ShoppingCart/      → ak-shoppingcart-api
# AK.Order/             → ak-order-api
# AK.UserIdentity/      → ak-useridentity-api
# AK.Payments/          → ak-payments-api
# AK.Notification/      → ak-notification-api
# AK.BuildingBlocks/    → ALL services (rebuild everything)
# docker-compose.yml    → ALL services
# keycloak/             → keycloak only (no rebuild needed — config hot-reloaded)
# ocelot.json           → ak-gateway-api only
```

---

## Step 2 — Rebuild Affected Services

```bash
# Rebuild specific services (fastest)
docker-compose up --build -d ak-gateway-api ak-order-api

# Rebuild everything (use only when BuildingBlocks or compose changed)
docker-compose up --build -d
```

---

## Step 3 — Wait for All Containers to Be Healthy

Infrastructure containers (Postgres, Redis, RabbitMQ, Keycloak, MongoDB, Elasticsearch) have Docker healthchecks. App services wait for them via `depends_on: condition: service_healthy`.

```bash
# Watch status every 3 seconds until all are healthy/up
watch -n 3 'docker ps --format "table {{.Names}}\t{{.Status}}" | sort'

# Or one-shot check
docker ps --format "table {{.Names}}\t{{.Status}}" | sort
```

**Expected states:**

| Container | Expected Status |
|-----------|-----------------|
| antkart-keycloak | Up (healthy) |
| antkart-mongodb | Up (healthy) |
| antkart-redis | Up (healthy) |
| antkart-postgres | Up (healthy) |
| antkart-rabbitmq | Up (healthy) |
| antkart-elasticsearch | Up (healthy) |
| antkart-mailhog | Up |
| antkart-kibana | Up |
| antkart-products-api | Up |
| antkart-discount-grpc | Up |
| antkart-shoppingcart-api | Up |
| antkart-order-api | Up |
| antkart-useridentity-api | Up |
| antkart-payments-api | Up |
| antkart-notification-api | Up |
| antkart-gateway-api | Up |

If any container is `Restarting` or `Exited`, check its logs immediately (Step 4).

---

## Step 4 — Diagnose a Failing Container

```bash
# See why a container is crashing
docker logs antkart-<service> --tail 50

# Most common causes:
# "Connection refused" / "ECONNREFUSED" → DB/Redis/Rabbit not ready yet; wait or check healthcheck
# "UnhandledPromise" or exception on startup → code bug introduced in this change
# "Port already in use" → another process holds the port; stop it or change the port
# "migrate: relation does not exist" → EF migration failed; check migration SQL
# "401 Unauthorized" from Keycloak → Keycloak not fully initialised; wait 30s and restart the app container
```

---

## Step 5 — Smoke-Test All Gateway Routes

Run this after all containers are up:

```bash
# Get a token
TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sectest_user","password":"SecTest@123"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))")

echo "=== Gateway Smoke Tests ==="

check_route() {
  local label=$1 url=$2 expected=$3 auth=${4:-""}
  if [ -n "$auth" ]; then
    code=$(curl -s -o/dev/null -w "%{http_code}" "$url" -H "Authorization: Bearer $auth")
  else
    code=$(curl -s -o/dev/null -w "%{http_code}" "$url")
  fi
  [ "$code" = "$expected" ] \
    && echo "✅ $label → $code" \
    || echo "❌ $label → expected $expected, got $code"
}

# Health routes (no auth)
check_route "Gateway health"         "http://localhost:8000/health"                        "200"
check_route "Products health"        "http://localhost:8000/gateway/health/products"        "200"
check_route "Orders health"          "http://localhost:8000/gateway/health/orders"          "200"
check_route "Cart health"            "http://localhost:8000/gateway/health/cart"            "200"
check_route "Payments health"        "http://localhost:8000/gateway/health/payments"        "200"
check_route "Notifications health"   "http://localhost:8000/gateway/health/notifications"  "200"
check_route "Identity health"        "http://localhost:8000/gateway/health/identity"        "200"

# Public routes (no auth)
check_route "Products (public GET)"  "http://localhost:8000/gateway/products"              "200"

# Authenticated routes
check_route "Cart (authenticated)"         "http://localhost:8000/gateway/cart"                      "200" "$TOKEN"
check_route "Orders/me (authenticated)"    "http://localhost:8000/gateway/orders/me"                 "200" "$TOKEN"
check_route "Payments/me (authenticated)"  "http://localhost:8000/gateway/payments/me"               "200" "$TOKEN"
check_route "Notifications (authenticated)" "http://localhost:8000/gateway/notifications"            "200" "$TOKEN"

# Unauthenticated → expect 401
check_route "Cart (no token → 401)"       "http://localhost:8000/gateway/cart"                      "401"
check_route "Orders/me (no token → 401)"  "http://localhost:8000/gateway/orders/me"                 "401"
```

---

## Step 6 — Check for Startup Migration Errors (EF Services)

```bash
# Order, Payments, Notification auto-migrate on startup
for svc in antkart-order-api antkart-payments-api antkart-notification-api; do
  echo "=== $svc migrations ==="
  docker logs $svc 2>&1 | grep -iE "migrat|error|exception" | head -10
done
```

Expected: `Applying pending EF Core migrations...` followed by migration names. No `ERROR` or `Exception` lines.

---

## Step 7 — Verify RabbitMQ Queues

All expected queues should be present after services start:

```bash
# List queues via management API
curl -s -u guest:guest http://localhost:15672/api/queues | \
  python3 -c "
import sys, json
queues = json.load(sys.stdin)
for q in sorted(q['name'] for q in queues if not q['name'].startswith('masstransit')):
    print(q)
"
```

Expected queues (one per consumer per service, prefixed by service name):
`notification-order-created`, `notification-order-confirmed`, `notification-order-cancelled`, `notification-payment-succeeded`, `notification-payment-failed`, `notification-user-registered`, `order-payment-succeeded`, `order-payment-failed`, `cart-order-confirmed`

---

## Step 8 — Final State Report

```bash
echo "=== Final Container State ==="
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | sort

echo ""
echo "=== Resource Usage ==="
docker stats --no-stream --format "table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}" | sort
```

---

## Checklist

- [ ] Changed files mapped to services — only those services rebuilt
- [ ] All 16 containers show Up or Up (healthy)
- [ ] No containers in Restarting or Exited state
- [ ] All Gateway health routes return 200
- [ ] Authenticated routes return 200 with valid token
- [ ] Unauthenticated routes return 401 without token
- [ ] EF migration logs show no errors
- [ ] RabbitMQ queues all present
- [ ] No unexpected OOM or CPU spikes in `docker stats`
