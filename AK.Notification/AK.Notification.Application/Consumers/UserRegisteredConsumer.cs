using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a welcome email when a new user registers.
// Triggered by UserRegisteredIntegrationEvent. In the Entra-native model the platform no longer
// hosts an identity service, so this event is sourced externally (e.g. an Entra/Graph signal on
// user provisioning); the consumer is retained as the welcome-notification seam for that wiring.
public sealed class UserRegisteredConsumer(IMediator mediator) : IConsumer<UserRegisteredIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredIntegrationEvent> context)
    {
        var msg = context.Message;
        await mediator.Send(new SendNotificationCommand(
            msg.UserId,
            NotificationChannel.Email,
            NotificationTemplateType.WelcomeEmail,
            msg.CustomerEmail,
            new WelcomeEmailModel(msg.CustomerName)),
            context.CancellationToken);
    }
}
