using AK.Notification.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationEntity = AK.Notification.Domain.Entities.Notification;

namespace AK.Notification.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .IsRequired();

        builder.Property(n => n.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Channel)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(n => n.TemplateType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(n => n.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(n => n.RecipientAddress)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(n => n.Subject)
            .HasMaxLength(512);

        builder.Property(n => n.Body)
            .IsRequired();

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(2048);

        builder.Property(n => n.SentAt);

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.Property(n => n.UpdatedAt);

        builder.Property(n => n.RetryCount)
            .IsRequired();

        builder.HasIndex(n => n.UserId);
        builder.HasIndex(n => n.CreatedAt);
    }
}
