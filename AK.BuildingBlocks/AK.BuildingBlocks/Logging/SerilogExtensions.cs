using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Events;

namespace AK.BuildingBlocks.Logging;

public static class SerilogExtensions
{
    // Structured logging via Serilog. Logs go to the Console (collected by the platform's log
    // pipeline — Application Insights / Log Analytics in the cloud) and a local rolling file for
    // development. The cloud telemetry path is wired by Application Insights, not by this method.
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Environment.ApplicationName;
        var environment = builder.Environment.EnvironmentName;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"logs/{serviceName}-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        builder.Host.UseSerilog();
        return builder;
    }
}
