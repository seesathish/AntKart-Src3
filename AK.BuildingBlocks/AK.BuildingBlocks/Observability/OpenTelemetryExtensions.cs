using System.Reflection;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AK.BuildingBlocks.Observability;

// OpenTelemetry wiring shared by every service. Distributed traces are exported to
// Application Insights (Azure Monitor); metrics are exposed on a Prometheus scrape
// endpoint. OTel propagates W3C traceparent automatically across HttpClient, gRPC and
// MassTransit, so a request is correlated end-to-end without hand-plumbing headers.
// Logs are NOT exported here — Serilog already owns logging (see SerilogExtensions).
public static class OpenTelemetryExtensions
{
    // Vaulted secret ApplicationInsights--ConnectionString maps to this config key.
    private const string ConnectionStringKey = "ApplicationInsights:ConnectionString";

    public static IHostApplicationBuilder AddOpenTelemetryObservability(
        this IHostApplicationBuilder builder, string serviceName)
    {
        var serviceVersion = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString() ?? "unknown";
        var environmentName = builder.Environment.EnvironmentName;
        var connectionString = builder.Configuration[ConnectionStringKey];
        var hasAzureMonitor = !string.IsNullOrWhiteSpace(connectionString);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", environmentName)
                }))
            .WithTracing(tracing =>
            {
                tracing
                    // Health probes must not become traces — every probe would bill App Insights for no value.
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !IsHealthEndpoint(context.Request.Path))
                    .AddHttpClientInstrumentation()   // Order -> Products (HttpCatalogPriceProvider) carries traceparent
                    .AddGrpcClientInstrumentation()    // Products -> Discount (IDiscountGrpcClient) carries traceparent
                    // Data-store spans via each driver's own ActivitySource / instrumentation:
                    .AddSource("MassTransit")          // Service Bus publish/consume spans
                    .AddSource("Npgsql")              // PostgreSQL command spans (Order/Payments/Discount)
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources") // Cosmos (Mongo API) command spans (Products)
                    // Redis command spans (ShoppingCart). The DI overload resolves IConnectionMultiplexer
                    // from the service provider when present; this extension is shared by all six services,
                    // and services without Redis registered are unaffected (no multiplexer → nothing instrumented).
                    .AddRedisInstrumentation();

                if (hasAzureMonitor)
                    tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString);
            })
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        if (!hasAzureMonitor)
            // Local development has no Key Vault; the service must still start. Traces are
            // collected in-process but not exported until the connection string is present.
            Serilog.Log.Warning(
                "OpenTelemetry: no {Key} configured for {ServiceName} — traces are collected but NOT exported to Application Insights (expected in local development).",
                ConnectionStringKey, serviceName);

        return builder;
    }

    // Prometheus scrape endpoint at /metrics (the exporter's default path).
    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint();
        return app;
    }

    // Matches /health, /health/live, /health/ready, /health/deps (segment-boundary aware,
    // so it does not match e.g. /healthz).
    private static bool IsHealthEndpoint(PathString path) => path.StartsWithSegments("/health");
}
