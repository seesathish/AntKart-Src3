using AK.BuildingBlocks.Email;
using AK.Notification.Application.Channels;
using AK.Notification.Domain.Enums;
using AK.Notification.Infrastructure.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AK.Notification.Tests.Infrastructure;

public sealed class EmailNotificationChannelTests
{
    [Fact]
    public void Channel_IsEmail()
    {
        var channel = new EmailNotificationChannel(Mock.Of<IEmailSender>(), NullLogger<EmailNotificationChannel>.Instance);
        channel.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task SendAsync_DelegatesToEmailSender_WithMappedFields()
    {
        var sender = new Mock<IEmailSender>();
        var channel = new EmailNotificationChannel(sender.Object, NullLogger<EmailNotificationChannel>.Instance);

        var message = new NotificationMessage(
            "bob@example.com", "Payment failed", "Your payment could not be processed.", NotificationChannel.Email);

        await channel.SendAsync(message);

        sender.Verify(s => s.SendAsync(
            "bob@example.com",
            "Payment failed",
            null,                                   // template body is plain text → no HTML part
            "Your payment could not be processed.",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
