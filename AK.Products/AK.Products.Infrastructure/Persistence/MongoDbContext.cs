using AK.Products.Infrastructure.Persistence;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace AK.Products.Infrastructure.Persistence;

public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        _database = client.GetDatabase(settings.Value.DatabaseName);
        CreateIndexes();
    }

    public MongoDbContext(IMongoDatabase database)
    {
        _database = database;
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName) =>
        _database.GetCollection<T>(collectionName);

    private void CreateIndexes()
    {
        var products = _database.GetCollection<Domain.Entities.Product>("products");

        var skuIndex = Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.SKU);
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            skuIndex, new CreateIndexOptions { Unique = true, Name = "sku_unique" }));

        var categoryIndex = Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.CategoryName);
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            categoryIndex, new CreateIndexOptions { Name = "idx_category" }));

        var statusIndex = Builders<Domain.Entities.Product>.IndexKeys.Ascending(p => p.Status);
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            statusIndex, new CreateIndexOptions { Name = "idx_status" }));

        var textIndex = Builders<Domain.Entities.Product>.IndexKeys
            .Text(p => p.Name).Text(p => p.Brand).Text(p => p.Description);
        products.Indexes.CreateOne(new CreateIndexModel<Domain.Entities.Product>(
            textIndex, new CreateIndexOptions { Name = "text_search" }));
    }
}
