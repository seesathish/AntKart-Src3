using AK.Notification.Application.Channels;
using AK.Notification.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Infrastructure.Channels;

// SMS channel stub — not yet implemented.
// Registered in DI so NotificationChannelResolver can resolve it and the service compiles
// without gaps in the channel enum. When SMS is activated, replace SendAsync with a call
// to Twilio, AWS SNS, or whichever SMS provider is chosen; no other code needs to change.
internal sealed class SmsNotificationChannel(ILogger<SmsNotificationChannel> logger) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Sms;

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("SMS stub: would send to {Address}", message.RecipientAddress);
        return Task.CompletedTask;
    }
}
