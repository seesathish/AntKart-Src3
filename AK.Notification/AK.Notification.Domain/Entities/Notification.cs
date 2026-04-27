using AK.BuildingBlocks.DDD;
using AK.Notification.Domain.Enums;

namespace AK.Notification.Domain.Entities;

// Notification is the aggregate root for the AK.Notification bounded context.
// A Notification record is created in a Pending state BEFORE delivery is attempted so that
// the send operation is auditable even if the email/SMS channel throws an error.
// Status lifecycle: Pending → Sent (success) or Pending → Failed (delivery error).
// RetryCount tracks how many times delivery was attempted — used by potential future
// retry background jobs to decide whether to give up.
public sealed class Notification : Entity, IAggregateRoot
{
    public string UserId { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public NotificationTemplateType TemplateType { get; private set; }
    public NotificationStatus Status { get; private set; }
    // RecipientAddress holds the email address, phone number, or WhatsApp ID depending on Channel.
    public string RecipientAddress { get; private set; } = string.Empty;
    public string? Subject { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }

    // Parameterless constructor required by EF Core to reconstruct entities from the database.
    // Private so application code must use the Create factory method.
    private Notification() { }

    // Factory method enforces invariants: userId and recipientAddress must be non-empty.
    // All new notifications start as Pending — the channel send happens after persistence.
    public static Notification Create(
        string userId,
        NotificationChannel channel,
        NotificationTemplateType templateType,
        string recipientAddress,
        string? subject,
        string body)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        if (string.IsNullOrWhiteSpace(recipientAddress))
            throw new ArgumentException("RecipientAddress cannot be empty.", nameof(recipientAddress));

        return new Notification
        {
            UserId = userId,
            Channel = channel,
            TemplateType = templateType,
            Status = NotificationStatus.Pending,
            RecipientAddress = recipientAddress,
            Subject = subject,
            Body = body,
            RetryCount = 0
        };
    }

    // Called by the command handler after the channel successfully delivers the message.
    // Guards against double-marking to catch bugs in retry logic.
    public void MarkSent()
    {
        if (Status == NotificationStatus.Sent)
            throw new InvalidOperationException("Notification is already marked as sent.");

        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
    }

    // Called when delivery throws — records the error for debugging and ops visibility.
    // Does NOT throw; the command handler decides whether to rethrow after calling this.
    public void MarkFailed(string error)
    {
        Status = NotificationStatus.Failed;
        ErrorMessage = error;
    }

    // Increments the attempt counter. Separated from MarkFailed so the caller controls
    // when and whether to count a retry (e.g. after a transient network error).
    public void IncrementRetry()
    {
        RetryCount++;
    }
}
