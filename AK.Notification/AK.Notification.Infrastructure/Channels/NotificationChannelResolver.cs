using AK.Notification.Application.Channels;
using AK.Notification.Domain.Enums;

namespace AK.Notification.Infrastructure.Channels;

// Resolves the correct INotificationChannel implementation at runtime based on the channel enum.
// All channel implementations (Email, SMS, WhatsApp) are registered in DI and injected as a collection.
// Calling Resolve(NotificationChannel.Email) returns the EmailNotificationChannel instance.
//
// Adding a new channel: implement INotificationChannel, register in DI — no changes needed here.
internal sealed class NotificationChannelResolver(IEnumerable<INotificationChannel> channels)
    : INotificationChannelResolver
{
    // Single() throws if no channel matches or if multiple implementations claim the same channel type.
    public INotificationChannel Resolve(NotificationChannel channel)
        => channels.Single(c => c.Channel == channel);
}
