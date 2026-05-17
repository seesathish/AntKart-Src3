# Skill: Add a Gateway Route

**Purpose:** Add a new Ocelot route to `ocelot.json` (Docker) and `ocelot.Development.json` (local dev) with the correct downstream host, auth policy, rate limiting, and QoS circuit breaker. Includes health check route and development override.

---

## When to Use
- A new service has been added and needs to be reachable through the Gateway
- A new endpoint group within an existing service needs its own rate limit or auth policy
- A route's rate limit or circuit breaker needs tuning

---

## Step 1 — Know the Route Details

Before editing any JSON, answer:

| Question | Example |
|----------|---------|
| What is the upstream path pattern? | `/gateway/reviews/{everything}` |
| What HTTP methods are allowed? | `["GET","POST","PUT","DELETE"]` |
| What is the downstream path? | `/api/reviews/{everything}` |
| What is the Docker service hostname? | `ak-reviews-api` |
| What is the downstream port? | `8080` (all REST services use 8080 internally) |
| Does it require authentication? | Yes (most), No (Products GET is public) |
| What rate limit? | See table below |
| What is the local dev port? | `5088` (check `launchSettings.json`) |

**Rate limit guidelines:**

| Traffic type | Suggested limit |
|-------------|-----------------|
| Public read-only (Products) | 20/1s |
| User mutations (Cart, Orders) | 10/1s |
| Auth endpoints (Identity) | 30/1s |
| Payment/financial | 30/1s |
| Notifications | 20/1s |

---

## Step 2 — Add the Route to ocelot.json

Open `AK.Gateway/AK.Gateway.API/ocelot.json`. Add inside the `"Routes"` array. Use an existing route as your template — do not create the structure from memory.

```json
{
  "UpstreamPathTemplate": "/gateway/reviews/{everything}",
  "UpstreamHttpMethod": [ "GET", "POST", "PUT", "DELETE" ],
  "DownstreamPathTemplate": "/api/reviews/{everything}",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [
    { "Host": "ak-reviews-api", "Port": 8080 }
  ],
  "AuthenticationOptions": {
    "AuthenticationProviderKey": "Bearer"
  },
  "RateLimitOptions": {
    "EnableRateLimiting": true,
    "Period": "1s",
    "PeriodTimespan": 1,
    "Limit": 20
  },
  "QoSOptions": {
    "ExceptionsAllowedBeforeBreaking": 5,
    "DurationOfBreak": 30000,
    "TimeoutValue": 10000
  }
}
```

**If the route is public (no auth required):** Omit the `"AuthenticationOptions"` block entirely. Example: Products GET routes.

**If you need separate limits for collection vs. single-resource routes:**
```json
{ "UpstreamPathTemplate": "/gateway/reviews",          "Limit": 20 },
{ "UpstreamPathTemplate": "/gateway/reviews/{everything}", "Limit": 10 }
```

---

## Step 3 — Add a Health Check Route

Always add a paired health route so the monitoring dashboard can check the downstream service via the Gateway:

```json
{
  "UpstreamPathTemplate": "/gateway/health/reviews",
  "UpstreamHttpMethod": [ "GET" ],
  "DownstreamPathTemplate": "/health",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [
    { "Host": "ak-reviews-api", "Port": 8080 }
  ]
}
```

Health routes do not need auth or rate limiting.

---

## Step 4 — Add the Development Override

Open `AK.Gateway/AK.Gateway.API/ocelot.Development.json`. Add the same route with `localhost` and the dev port:

```json
{
  "Routes": [
    {
      "UpstreamPathTemplate": "/gateway/reviews/{everything}",
      "DownstreamHostAndPorts": [
        { "Host": "localhost", "Port": 5088 }
      ]
    },
    {
      "UpstreamPathTemplate": "/gateway/health/reviews",
      "DownstreamHostAndPorts": [
        { "Host": "localhost", "Port": 5088 }
      ]
    }
  ]
}
```

The development override only needs to specify `UpstreamPathTemplate` and `DownstreamHostAndPorts` — Ocelot merges the rest from `ocelot.json`. The override file is loaded last, so these values win in `Development` environment.

---

## Step 5 — Check for Upstream Path Conflicts

Ocelot matches routes top-to-bottom. A wildcard route like `/gateway/reviews/{everything}` can shadow a more specific route `/gateway/reviews/admin` if it appears first.

```bash
# List all current upstream patterns in order
python3 -c "
import json
with open('AK.Gateway/AK.Gateway.API/ocelot.json') as f:
    d = json.load(f)
for r in d.get('Routes', []):
    print(r['UpstreamPathTemplate'])
"
```

**Rule:** More specific routes (e.g. `/gateway/reviews/admin`) must appear **before** wildcard routes (e.g. `/gateway/reviews/{everything}`) in the JSON array.

---

## Step 6 — Verify Rate Limiting is Active

Rate limiting requires `AddMemoryCache()` registered in Gateway `Program.cs` (already done — see commit fixing VULN-001). Confirm it is present:

```bash
grep "AddMemoryCache" AK.Gateway/AK.Gateway.API/Program.cs
```

If missing, add `builder.Services.AddMemoryCache();` before `builder.Services.AddOcelot(...)`.

---

## Step 7 — Rebuild Gateway and Test

```bash
# Rebuild only the gateway
docker-compose up --build -d ak-gateway-api

# Wait for startup
sleep 5

# Smoke test the new route
curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/gateway/reviews \
  -H "Authorization: Bearer <token>"
# Expect: 200 (or 401 if no token and route requires auth)

# Test health route
curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/gateway/health/reviews
# Expect: 200

# Test rate limit (parallel burst above the limit)
tmpdir=$(mktemp -d)
LIMIT=20; BURST=$((LIMIT + 10))
for i in $(seq 1 $BURST); do
  curl -s -o /dev/null -w "%{http_code}" http://localhost:8000/gateway/reviews \
    -H "Authorization: Bearer <token>" > "$tmpdir/$i.out" &
done
wait
RL=$(grep -l "429" "$tmpdir"/*.out | wc -l)
echo "Rate-limited: $RL / $BURST (expect >0)"
rm -rf "$tmpdir"
```

---

## Step 8 — Update Documentation

- `AK.Gateway/API_GATEWAY.md` — add the new service to the routing table
- `README.md` — if this is for a new service, update the Authorization table
- `CLAUDE.md` — update the Gateway Completed Services section if a new route group was added

---

## Checklist

- [ ] Route added to `ocelot.json` with all required fields (upstream, downstream, auth, rate limit, QoS)
- [ ] Health check route added to `ocelot.json`
- [ ] Development override added to `ocelot.Development.json`
- [ ] No upstream path conflicts (specific routes before wildcard routes)
- [ ] `AddMemoryCache()` confirmed in Gateway `Program.cs`
- [ ] Gateway rebuilt (`docker-compose up --build -d ak-gateway-api`)
- [ ] Smoke test: route returns expected status code
- [ ] Rate limit test: burst above limit produces 429s
- [ ] `API_GATEWAY.md` routing table updated
- [ ] `README.md` Authorization table updated if applicable
