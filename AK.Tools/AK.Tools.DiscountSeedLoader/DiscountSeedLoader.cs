using Microsoft.Extensions.Logging;

namespace AK.Tools.DiscountSeedLoader;

// One concrete example surfaced in the summary so it can be used as a known E2E test case.
public sealed record SeedExample(string Sku, string ProductId, string Discount, SelectionRule Rule);

// The run summary: totals by rule plus a handful of concrete examples.
public sealed record SeedSummary(
    int ProductsRead,
    int KidsCoupons,
    int MenShirtsCoupons,
    int SpreadCoupons,
    IReadOnlyList<SeedExample> Examples)
{
    public int TotalCoupons => KidsCoupons + MenShirtsCoupons + SpreadCoupons;
}

// Orchestrates: read products from the source, run the deterministic selection, upsert each coupon,
// and produce a summary. The I/O (Cosmos read, EF write) is behind interfaces, so this is testable.
public sealed class DiscountSeedLoader
{
    private readonly IProductSource _products;
    private readonly ICouponUpsertSink _sink;
    private readonly ILogger<DiscountSeedLoader> _logger;

    public DiscountSeedLoader(IProductSource products, ICouponUpsertSink sink, ILogger<DiscountSeedLoader> logger)
    {
        _products = products;
        _sink = sink;
        _logger = logger;
    }

    public async Task<SeedSummary> RunAsync(CancellationToken ct = default)
    {
        var products = await _products.GetProductsAsync(ct);
        _logger.LogInformation("Read {Count} products from Cosmos.", products.Count);

        var selected = CouponSelector.Select(products, DateTime.UtcNow);

        var written = 0;
        foreach (var s in selected)
        {
            await _sink.UpsertAsync(s.Coupon, ct);
            written++;
            if (written % 200 == 0)
                _logger.LogInformation("Upserted {Count} coupons...", written);
        }

        var summary = BuildSummary(products.Count, selected);
        LogSummary(summary);
        return summary;
    }

    // Picks a deterministic, representative set of examples (across both rules) — ordered by SKU so
    // the same products surface on every run.
    private static SeedSummary BuildSummary(int productsRead, IReadOnlyList<SelectedCoupon> selected)
    {
        var ordered = selected.OrderBy(x => x.Product.Sku, StringComparer.Ordinal).ToList();

        var examples = new List<SeedExample>();
        examples.AddRange(Pick(ordered, SelectionRule.KidsCategory, 2));
        examples.AddRange(Pick(ordered, SelectionRule.MenShirts, 1));
        examples.AddRange(Pick(ordered, SelectionRule.DeterministicSpread, 2));

        return new SeedSummary(
            productsRead,
            KidsCoupons: selected.Count(x => x.Rule == SelectionRule.KidsCategory),
            MenShirtsCoupons: selected.Count(x => x.Rule == SelectionRule.MenShirts),
            SpreadCoupons: selected.Count(x => x.Rule == SelectionRule.DeterministicSpread),
            Examples: examples);
    }

    private static IEnumerable<SeedExample> Pick(IEnumerable<SelectedCoupon> ordered, SelectionRule rule, int count) =>
        ordered.Where(x => x.Rule == rule).Take(count).Select(x => new SeedExample(
            x.Product.Sku,
            x.Coupon.ProductId,
            CouponSelector.Describe(x.Coupon.Amount, x.Coupon.DiscountType, x.Product.Currency),
            rule));

    private void LogSummary(SeedSummary s)
    {
        _logger.LogInformation(
            "Seed summary: {Products} products read, {Total} coupons upserted (Kids={Kids}, Men/Shirts={MenShirts}, deterministic spread={Spread}).",
            s.ProductsRead, s.TotalCoupons, s.KidsCoupons, s.MenShirtsCoupons, s.SpreadCoupons);

        _logger.LogInformation("Example coupons (SKU -> ProductId -> discount) — usable as E2E test cases:");
        foreach (var e in s.Examples)
            _logger.LogInformation("  [{Rule}] {Sku} -> {ProductId} -> {Discount}", e.Rule, e.Sku, e.ProductId, e.Discount);
    }
}
