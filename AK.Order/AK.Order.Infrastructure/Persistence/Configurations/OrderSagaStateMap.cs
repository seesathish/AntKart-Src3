using AK.Order.Application.Sagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AK.Order.Infrastructure.Persistence.Configurations;

public sealed class OrderSagaStateMap : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.ToTable("order_saga_states");
        builder.HasKey(x => x.CorrelationId);
        builder.Property(x => x.CurrentState).HasMaxLength(64);
        builder.Property(x => x.UserId).HasMaxLength(256);
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
