using AK.Products.Application.Interfaces;
using AK.Products.Domain.Entities;
using AK.Products.Domain.Specifications;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace AK.Products.Infrastructure.Persistence.Repositories;

public sealed class ProductRepository : IProductRepository
{
    private readonly IMongoCollection<Product> _collection;

    public ProductRepository(MongoDbContext context, IOptions<MongoDbSettings> settings)
    {
        _collection = context.GetCollection<Product>(settings.Value.ProductsCollection);
    }

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _collection.Find(p => p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default) =>
        await _collection.Find(p => p.SKU == sku).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
    {
        var results = await _collection.Find(_ => true).ToListAsync(ct);
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        var results = await _collection.Find(p => p.CategoryName == category).ToListAsync(ct);
        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Empty;
        var results = await _collection.DistinctAsync<string>("CategoryName", filter, null, ct);
        var list = await results.ToListAsync(ct);
        return list.OrderBy(c => c).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<Product>> ListAsync(ISpecification<Product> spec, CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Where(spec.Criteria);
        var query = _collection.Find(filter);

        if (spec.OrderBy is not null)
            query = query.SortBy(spec.OrderBy);
        else if (spec.OrderByDescending is not null)
            query = query.SortByDescending(spec.OrderByDescending);

        if (spec.IsPagingEnabled)
            query = query.Skip(spec.Skip).Limit(spec.Take);

        var results = await query.ToListAsync(ct);
        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(ISpecification<Product> spec, CancellationToken ct = default)
    {
        var filter = Builders<Product>.Filter.Where(spec.Criteria);
        return (int)await _collection.CountDocumentsAsync(filter, null, ct);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(product, null, ct);
        return product;
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default) =>
        await _collection.ReplaceOneAsync(p => p.Id == product.Id, product, new ReplaceOptions(), ct);

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await _collection.DeleteOneAsync(p => p.Id == id, ct);

    public async Task BulkInsertAsync(IEnumerable<Product> products, CancellationToken ct = default) =>
        await _collection.InsertManyAsync(products, null, ct);

    public async Task BulkUpdateAsync(IEnumerable<Product> products, CancellationToken ct = default)
    {
        var writeModels = products.Select(p =>
            new ReplaceOneModel<Product>(Builders<Product>.Filter.Where(x => x.Id == p.Id), p));
        await _collection.BulkWriteAsync(writeModels, null, ct);
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default) =>
        await _collection.CountDocumentsAsync(p => p.Id == id, null, ct) > 0;

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken ct = default) =>
        await _collection.CountDocumentsAsync(p => p.SKU == sku, null, ct) > 0;

    public async Task<IReadOnlyList<Product>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var results = await _collection.Find(_ => true)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
        return results.AsReadOnly();
    }
}
