using AK.Products.Domain.Common;
using AK.Products.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace AK.Products.Infrastructure.Persistence;

internal static class ProductClassMap
{
    private static bool _registered;
    private static readonly object _lock = new();

    public static void Register()
    {
        lock (_lock)
        {
            if (_registered) return;

            BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<Product>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.UnmapProperty(p => p.DomainEvents);
            });

            _registered = true;
        }
    }
}
