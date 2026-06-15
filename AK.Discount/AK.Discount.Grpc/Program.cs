using AK.BuildingBlocks.Configuration;
using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.Discount.Application.Extensions;
using AK.Discount.Grpc.Interceptors;
using AK.Discount.Grpc.Services;
using AK.Discount.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Load configuration/secrets from Azure Key Vault (when KeyVault:Uri is set), using this
// service's own Entra identity, before anything reads configuration. This is how the vaulted
// ConnectionStrings--DiscountDb secret flows into IConfiguration as ConnectionStrings:DiscountDb
// and is read by AddDiscountInfrastructure — no secret is committed to the repo.
builder.Configuration.AddAzureKeyVaultConfiguration(builder.Configuration);

builder.AddSerilogLogging();
builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<AuthInterceptor>();
    opts.Interceptors.Add<ExceptionInterceptor>();
});
if (builder.Environment.IsDevelopment())
    builder.Services.AddGrpcReflection();
builder.Services.AddApplication();
builder.Services.AddDiscountInfrastructure(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddSingleton<AuthInterceptor>();

var app = builder.Build();
app.MapGrpcService<DiscountService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapDefaultHealthChecks();
app.MapGet("/", () => "AK.Discount gRPC service. Use a gRPC client.");
await app.MigrateAndSeedAsync();
app.Run();
public partial class Program { }
