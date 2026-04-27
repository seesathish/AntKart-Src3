using AK.BuildingBlocks.DDD;
using AK.Products.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace AK.Products.Infrastructure.Persistence;

// MongoDB requires explicit class mappings when domain entities don't have Bson attributes.
// We keep Bson attributes OUT of the domain layer to preserve Clean Architecture —
// the domain must not depend on any infrastructure concern.
// This class is registered once at startup (before MongoDbContext is built).
internal static class ProductClassMap
{
    private static bool _registered;

    // Lock ensures thread safety if multiple threads try to register at startup simultaneously.
    private static readonly object _lock = new();

    public static void Register()
    {
        lock (_lock)
        {
            if (_registered) return;  // Guard: RegisterClassMap throws if called twice for the same type.

            BsonClassMap.RegisterClassMap<StringEntity>(cm =>
            {
                cm.AutoMap();  // Automatically maps all public properties.
                // SetIgnoreExtraElements: if a MongoDB document has a field that doesn't
                // exist on the class (e.g. from an older schema version), ignore it instead of throwing.
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<Product>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);

                // DomainEvents is an in-memory list used for event dispatch — it must NOT be
                // serialised to MongoDB. Unmapping it prevents MongoDB from trying to store it.
                cm.UnmapProperty(p => p.DomainEvents);
            });

            _registered = true;
        }
    }
}
