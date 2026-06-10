using AK.BuildingBlocks.Messaging;
using AK.BuildingBlocks.Resilience;
using AK.Products.Application.Consumers;
using AK.Products.Application.Interfaces;
using AK.Products.Infrastructure.Grpc;
using AK.Products.Infrastructure.Persistence;
using AK.Products.Infrastructure.Persistence.Repositories;
using AK.Products.Infrastructure.Seeders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.Products.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ProductClassMap.Register();

        // Non-secret values (database + collection names) come from appsettings.
        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDbSettings"));

        // M3 Step 4: the Cosmos DB (MongoDB API) connection string is a SECRET — it is never
        // committed. It is loaded into configuration from Key Vault (by the Step 1 foundation)
        // under the secret name "ProductsCosmosConnectionString". When present it sets the
        // connection string; otherwise the non-secret local default (mongodb://localhost:27017)
        // applies, so offline development still works.
        services.PostConfigure<MongoDbSettings>(s =>
        {
            var cosmosConnectionString = configuration["ProductsCosmosConnectionString"];
            if (!string.IsNullOrWhiteSpace(cosmosConnectionString))
                s.ConnectionString = cosmosConnectionString;
        });

        services.AddSingleton<MongoDbContext>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ProductSeeder>();

        services.Configure<DiscountGrpcSettings>(configuration.GetSection("DiscountGrpc"));
        services.AddHttpClient("discount-grpc", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .AddHttpResilienceWithCircuitBreaker(maxRetryAttempts: 3, failureRatio: 0.5, minimumThroughput: 3, breakDurationSeconds: 30);
        services.AddScoped<IDiscountGrpcClient, DiscountGrpcClient>();

        services.AddAzureServiceBusMassTransit(configuration, "products", cfg =>
        {
            cfg.AddConsumer<ReserveStockConsumer>();
        });

        return services;
    }
}
