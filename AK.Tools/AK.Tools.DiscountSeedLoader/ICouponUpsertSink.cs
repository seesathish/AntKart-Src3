using AK.Discount.Domain.Entities;

namespace AK.Tools.DiscountSeedLoader;

// Where the loader writes each coupon. Abstracted so the orchestration can be unit-tested with a
// fake, no live database required.
public interface ICouponUpsertSink
{
    Task UpsertAsync(Coupon coupon, CancellationToken ct = default);
}
