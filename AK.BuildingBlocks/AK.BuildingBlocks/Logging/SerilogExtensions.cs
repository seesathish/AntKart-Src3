using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

namespace AK.BuildingBlocks.Logging;

public static class SerilogExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Environment.ApplicationName;
        var environment = builder.Environment.EnvironmentName;
        var esUrl = builder.Configuration["Elasticsearch:Url"];

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"logs/{serviceName}-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);

        if (!string.IsNullOrWhiteSpace(esUrl))
        {
            logConfig.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUrl))
            {
                AutoRegisterTemplate = true,
                AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
                IndexFormat = $"antkart-logs-{environment.ToLower()}-{{0:yyyy.MM}}",
                ModifyConnectionSettings = conn => conn.ServerCertificateValidationCallback(
                    (_, _, _, _) => true)
            });
        }

        Log.Logger = logConfig.CreateLogger();
        builder.Host.UseSerilog();
        return builder;
    }
}
