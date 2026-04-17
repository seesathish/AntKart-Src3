using AK.ShoppingCart.Application.Interfaces;
using AK.ShoppingCart.Infrastructure.Persistence;
using AK.ShoppingCart.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AK.ShoppingCart.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisSettings>(configuration.GetSection("RedisSettings"));
        services.AddSingleton<RedisContext>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }
}
