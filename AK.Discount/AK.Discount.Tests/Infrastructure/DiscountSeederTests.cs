using AK.Discount.Infrastructure.Persistence;
using AK.Discount.Infrastructure.Seeders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace AK.Discount.Tests.Infrastructure;

public sealed class DiscountSeederTests
{
    private static DbContextOptions<DiscountContext> GetOptions() =>
        new DbContextOptionsBuilder<DiscountContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task SeedAsync_WhenDatabaseIsEmpty_ShouldCreate300Coupons()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var logger = Mock.Of<ILogger<DiscountSeeder>>();
        var seeder = new DiscountSeeder(ctx, logger);

        await seeder.SeedAsync();

        ctx.Coupons.Count().Should().Be(300);
    }

    [Fact]
    public async Task SeedAsync_WhenAlreadySeeded_ShouldNotAddMore()
    {
        var opts = GetOptions();
        var logger = Mock.Of<ILogger<DiscountSeeder>>();

        using var ctx1 = new DiscountContext(opts);
        await new DiscountSeeder(ctx1, logger).SeedAsync();

        using var ctx2 = new DiscountContext(opts);
        await new DiscountSeeder(ctx2, logger).SeedAsync();

        using var readCtx = new DiscountContext(opts);
        readCtx.Coupons.Count().Should().Be(300);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateCouponsWithValidData()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var logger = Mock.Of<ILogger<DiscountSeeder>>();
        var seeder = new DiscountSeeder(ctx, logger);

        await seeder.SeedAsync();

        var coupons = ctx.Coupons.ToList();
        coupons.Should().AllSatisfy(c =>
        {
            c.ProductId.Should().NotBeNullOrEmpty();
            c.CouponCode.Should().NotBeNullOrEmpty();
            c.Amount.Should().BeGreaterThan(0);
            c.IsActive.Should().BeTrue();
        });
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateCouponsForAllGenders()
    {
        var opts = GetOptions();
        using var ctx = new DiscountContext(opts);
        var logger = Mock.Of<ILogger<DiscountSeeder>>();
        await new DiscountSeeder(ctx, logger).SeedAsync();

        var productIds = ctx.Coupons.Select(c => c.ProductId).ToList();
        productIds.Should().Contain(id => id.StartsWith("MEN-"));
        productIds.Should().Contain(id => id.StartsWith("WOM-"));
        productIds.Should().Contain(id => id.StartsWith("KID-"));
    }
}
