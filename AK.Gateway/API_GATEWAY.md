# AK.Gateway — API Gateway Technical Design

## Overview

The API Gateway is the single entry point for all AntKart REST services. Built with **Ocelot 23.4.2** on .NET 9, it provides routing, JWT passthrough authentication, per-route rate limiting, and circuit-breaker QoS — without any business logic.

---

## Architecture Position

```
Client (browser / mobile / Postman)
          │
          ▼  HTTP  port 8000 (Docker) / 8000 (dev)
  ┌───────────────────┐
  │   AK.Gateway.API  │  Ocelot + JWT validation
  └───────────────────┘
    │        │        │        │
    ▼        ▼        ▼        ▼
Products  ShoppingCart  Order  UserIdentity
 :8080      :8080       :8080    :8080
```

---

## Project Structure

```
AK.Gateway/
└── AK.Gateway.API/
    ├── AK.Gateway.API.csproj
    ├── Program.cs
    ├── Dockerfile
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── ocelot.json                   ← Docker / production routes
    └── ocelot.Development.json       ← Dev localhost routes
```

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Ocelot | 23.4.2 | API gateway routing, rate limiting, QoS |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.x | JWT validation |
| AK.BuildingBlocks | local | Serilog, health checks, correlation middleware |

---

## Route Configuration

`ocelot.json` defines 10 upstream → downstream route mappings:

| Upstream (Gateway) | Downstream | Auth Required |
|-------------------|------------|---------------|
| `GET /api/products` | `ak-products-api:8080` | No |
| `GET/POST /api/products/{id}` | `ak-products-api:8080` | POST: Yes |
| `GET/POST/PUT/DELETE /api/v1/cart` | `ak-shoppingcart-api:8080` | Yes |
| `GET/POST /api/orders` | `ak-order-api:8080` | Yes |
| `GET /api/orders/{id}` | `ak-order-api:8080` | Yes |
| `POST /api/auth/**` | `ak-useridentity-api:8080` | No |
| `/health` (all services) | downstream `/health` | No |

### Rate Limiting

```json
"RateLimitOptions": {
  "EnableRateLimiting": true,
  "Period": "1s",
  "Limit": 20
}
```

- Products: 20 RPS
- Cart / Orders: 10 RPS
- Identity: 30 RPS

### QoS (Circuit Breaker)

```json
"QoSOptions": {
  "ExceptionsAllowedBeforeBreaking": 5,
  "DurationOfBreak": 30000,
  "TimeoutValue": 10000
}
```

---

## Authentication Strategy

JWT is validated **twice** (defence in depth):

1. **At Gateway** — Ocelot checks `AuthenticationProviderKey: "Bearer"` and rejects unauthenticated requests with 401 before forwarding.
2. **At downstream** — each service independently validates the same JWT, so they remain secure if accessed directly (bypassing the gateway).

The `Authorization` header is forwarded verbatim; Keycloak tokens flow through unchanged.

---

## Program.cs Bootstrap

```csharp
builder.AddSerilogLogging();
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddOcelot()
    .AddDelegatingHandler<...>();

// ocelot.Development.json merged in Development environment
app.UseCorrelationIdMiddleware();
app.UseKeycloakAuth();
app.MapDefaultHealthChecks();
await app.UseOcelot();
```

---

## Docker

- **Port:** 9090 (host) → 8000 (container)
- **Dockerfile:** `AK.Gateway/AK.Gateway.API/Dockerfile`
- **Build context:** repo root
- **Depends on:** keycloak, ak-products-api, ak-shoppingcart-api, ak-order-api, ak-useridentity-api

---

## Development Overrides

`ocelot.Development.json` overrides downstream hosts to localhost dev ports:

```
Products  → http://localhost:5077
Cart      → http://localhost:5079
Orders    → http://localhost:5080
Identity  → http://localhost:5085
```

Load with:
```csharp
config.AddJsonFile("ocelot.Development.json", optional: true, reloadOnChange: true);
```
