using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Notification.Application.Commands;
using AK.Notification.Application.Templates;
using AK.Notification.Domain.Enums;
using MassTransit;
using MediatR;

namespace AK.Notification.Application.Consumers;

// Sends a welcome email when a new user registers.
// Triggered by UserRegisteredIntegrationEvent published by AK.UserIdentity after
// the user is created in Keycloak and the "user" role is assigned.
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
