using AK.Notification.Application.Channels;
using AK.Notification.Application.Repositories;
using AK.Notification.Application.Templates;
using MediatR;
using Microsoft.Extensions.Logging;
using NotificationEntity = AK.Notification.Domain.Entities.Notification;

namespace AK.Notification.Application.Commands;

// Central command handler for all outbound notifications.
// Called by every MassTransit consumer in this service after receiving an integration event.
//
// Steps:
//   1. Render the template (subject + body) based on TemplateType and Model data
//   2. Persist the notification record to PostgreSQL (status = Pending)
//   3. Resolve the correct channel (Email / SMS / WhatsApp) via INotificationChannelResolver
//   4. Send the notification
//   5. Update the record to Sent or Failed
//
// Failures are non-fatal: if the SMTP send fails, the notification is marked Failed in the DB
// and the error is logged, but no exception bubbles up to abort the MassTransit consumer.
// This prevents a transient SMTP error from replaying the entire event indefinitely.
public sealed class SendNotificationCommandHandler(
    INotificationRepository repository,
    INotificationChannelResolver channelResolver,
    INotificationTemplateRenderer templateRenderer,
    ILogger<SendNotificationCommandHandler> logger)
    : IRequestHandler<SendNotificationCommand, Guid>
{
    public async Task<Guid> Handle(SendNotificationCommand request, CancellationToken cancellationToken)
    {
        // Render the template: produces subject + body text from a typed model.
        var content = templateRenderer.Render(request.TemplateType, request.Model);

        var notification = NotificationEntity.Create(
            request.UserId,
            request.Channel,
            request.TemplateType,
            request.RecipientAddress,
            content.Subject,
            content.Body);

        // Persist first so we have a record even if delivery fails.
        await repository.AddAsync(notification, cancellationToken);

        // INotificationChannelResolver picks the right implementation:
        //   Email → EmailNotificationChannel (MailKit SMTP)
        //   SMS   → SmsNotificationChannel   (stub — not implemented yet)
        var channel = channelResolver.Resolve(request.Channel);
        var message = new NotificationMessage(
            notification.RecipientAddress,
            notification.Subject,
            notification.Body,
            notification.Channel);

        try
        {
            await channel.SendAsync(message, cancellationToken);
            notification.MarkSent();
        }
        catch (Exception ex)
        {
            // Log the delivery failure but don't rethrow — marking as Failed is enough.
            logger.LogError(ex, "Failed to send notification {NotificationId} via {Channel}", notification.Id, request.Channel);
            notification.MarkFailed(ex.Message);
        }

        // Update record with final Sent/Failed status.
        await repository.UpdateAsync(notification, cancellationToken);

        return notification.Id;
    }
}
