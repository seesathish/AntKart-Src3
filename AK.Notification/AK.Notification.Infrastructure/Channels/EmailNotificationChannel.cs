using AK.BuildingBlocks.Email;
using AK.Notification.Application.Channels;
using AK.Notification.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AK.Notification.Infrastructure.Channels;

// Email delivery channel. Delegates to the shared IEmailSender (Azure Communication Services),
// so this service and the serverless Function send through the same managed-identity-authenticated
// path — no SMTP server, no credentials in config. The local MailKit/Mailhog SMTP path it replaced
// was the pre-cloud placeholder.
//
// The template renderer produces a plain-text body, so it is passed as the plain-text part; ACS
// requires at least one of an HTML or plain-text body. Send failures propagate to
// SendNotificationCommandHandler, which records the notification as Failed (it wraps this call) —
// the consumer is never faulted.
internal sealed class EmailNotificationChannel(
    IEmailSender emailSender,
    ILogger<EmailNotificationChannel> logger)
    : INotificationChannel
{
    // Tells INotificationChannelResolver which channel type this class handles.
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        await emailSender.SendAsync(
            to: message.RecipientAddress,
            subject: message.Subject ?? string.Empty,
            htmlBody: null,
            plainTextBody: message.Body,
            ct: ct);

        logger.LogInformation(
            "Email dispatched to {Recipient} with subject '{Subject}'", message.RecipientAddress, message.Subject);
    }
}
