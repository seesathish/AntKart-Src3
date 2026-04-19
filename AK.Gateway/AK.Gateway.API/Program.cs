using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.BuildingBlocks.Middleware;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.AddSerilogLogging();
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddOcelot(builder.Configuration).AddPolly();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseKeycloakAuth();
app.MapDefaultHealthChecks();
await app.UseOcelot();
app.Run();

public partial class Program { }
