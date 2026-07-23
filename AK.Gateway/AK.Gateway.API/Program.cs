using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Middleware;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

// AK.Gateway is the single entry point for all external traffic.
// Clients never call downstream services (Products, Cart, Order, etc.) directly —
// everything goes through this gateway on port 9090 (Docker) / 8000 (dev).
//
// Ocelot routes are defined in ocelot.json (Docker) and ocelot.Development.json (dev).
// Each route specifies: upstream path, downstream service, rate limit, and QoS circuit breaker.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Load config sources in priority order (last wins on duplicate keys):
//   appsettings.json → appsettings.{Environment}.json → ocelot.json → ocelot.{Environment}.json → env vars
// ocelot.Development.json overrides downstream URLs to use localhost ports for local dev.
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.AddSerilogLogging();

// JWT validation at the gateway edge — Ocelot checks the Bearer token before forwarding the request.
// Downstream services also validate JWTs independently (defence in depth).
builder.Services.AddEntraAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();

// Required by Ocelot's rate limiting middleware — stores per-route request counters.
// Without this, EnableRateLimiting in ocelot.json is silently ignored.
builder.Services.AddMemoryCache();

// AddPolly enables Ocelot's QoS (circuit breaker) per route, configured in ocelot.json.
builder.Services.AddOcelot(builder.Configuration).AddPolly();

// AllowAll CORS: permits frontend apps from any origin to call the gateway.
// In production, replace AllowAnyOrigin with a specific allowed origins list.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// Attach X-Correlation-Id to every request so logs from all downstream services
// can be correlated in Kibana by the same ID.
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseEntraAuth();

// The gateway's OWN health endpoints — /health, /health/live, /health/ready, /health/deps —
// wired through the same shared AK.BuildingBlocks mechanism every other service uses, so the
// gateway is consistent with the platform. The registered check is the shallow "self" check
// (no downstream calls), so liveness/readiness never depend on a downstream service.
app.MapDefaultHealthChecks();

// CRITICAL — Ocelot's middleware is TERMINAL: any path that reaches it is treated as a proxy
// request, and anything without a matching downstream route gets a 404. Mapping the health
// endpoints "before" UseOcelot is NOT sufficient, because WebApplication defers endpoint
// EXECUTION to the end of the pipeline (UseEndpoints) — Ocelot short-circuits first, so
// /health/* would still 404 and the Kubernetes probe would restart-loop the pod.
//
// Fix: run Ocelot ONLY for non-/health paths via MapWhen, so /health/* bypasses Ocelot
// entirely and falls through to endpoint routing, which executes the mapped health checks.
// Every proxied upstream route is under /gateway/*, so excluding /health removes nothing
// Ocelot needs to serve. UseOcelot is async; it configures the branch pipeline once at
// startup, so resolving it synchronously here is safe (it is not a per-request call).
app.MapWhen(
    context => !context.Request.Path.StartsWithSegments("/health"),
    ocelotApp => ocelotApp.UseOcelot().GetAwaiter().GetResult());

app.Run();

public partial class Program { }
