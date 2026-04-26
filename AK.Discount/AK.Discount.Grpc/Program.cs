using AK.BuildingBlocks.HealthChecks;
using AK.BuildingBlocks.Logging;
using AK.Discount.Application.Extensions;
using AK.Discount.Grpc.Interceptors;
using AK.Discount.Grpc.Services;
using AK.Discount.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddSerilogLogging();
builder.Services.AddGrpc(opts =>
{
    opts.Interceptors.Add<AuthInterceptor>();
    opts.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddApplication();
builder.Services.AddDiscountInfrastructure(builder.Configuration);
builder.Services.AddDefaultHealthChecks();
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddSingleton<AuthInterceptor>();

var app = builder.Build();
app.MapGrpcService<DiscountService>();
app.MapDefaultHealthChecks();
app.MapGet("/", () => "AK.Discount gRPC service. Use a gRPC client.");
await app.MigrateAndSeedAsync();
app.Run();
public partial class Program { }
