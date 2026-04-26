using AK.Notification.Application.Channels;
using AK.Notification.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Infrastructure.Channels;

// WhatsApp channel stub — not yet implemented.
// Registered in DI alongside Email and SMS so the resolver can find it.
// To activate: replace SendAsync with a call to the WhatsApp Business API (Meta Cloud API
// or a provider like Twilio WhatsApp). RecipientAddress should be the E.164 phone number.
internal sealed class WhatsAppNotificationChannel(ILogger<WhatsAppNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("WhatsApp stub: would send to {Address}", message.RecipientAddress);
        return Task.CompletedTask;
    }
}
