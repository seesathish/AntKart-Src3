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
        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDbSettings"));
        services.AddSingleton<MongoDbContext>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ProductSeeder>();

        services.Configure<DiscountGrpcSettings>(configuration.GetSection("DiscountGrpc"));
        services.AddSingleton<IDiscountGrpcClient, DiscountGrpcClient>();

        return services;
    }
}
