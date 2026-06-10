using AK.BuildingBlocks.Email;
using Azure;
using Azure.Communication.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AK.Notification.Tests.Infrastructure;

public sealed class AcsEmailSenderTests
{
    private static AcsEmailSender CreateSender(Mock<EmailClient> client, string? displayName = "AntKart")
    {
        var options = Options.Create(new AcsEmailOptions
        {
            Endpoint = "https://acs-antkart-dev.unitedstates.communication.azure.com",
            SenderAddress = "DoNotReply@managed.azurecomm.net",
            SenderDisplayName = displayName
        });
        return new AcsEmailSender(client.Object, options, NullLogger<AcsEmailSender>.Instance);
    }

    [Fact]
    public async Task SendAsync_Configured_SendsMessageWithMappedFields()
    {
        EmailMessage? captured = null;
        var client = new Mock<EmailClient>();
        client
            .Setup(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WaitUntil, EmailMessage, CancellationToken>((_, m, _) => captured = m)
            .ReturnsAsync((EmailSendOperation)null!);

        var sender = CreateSender(client);
        await sender.SendAsync("alice@example.com", "Order confirmed", "<p>hi</p>", "hi");

        captured.Should().NotBeNull();
        captured!.SenderAddress.Should().Be("AntKart <DoNotReply@managed.azurecomm.net>");
        captured.Recipients.To.Should().ContainSingle(r => r.Address == "alice@example.com");
        captured.Content.Subject.Should().Be("Order confirmed");
        captured.Content.Html.Should().Be("<p>hi</p>");
        captured.Content.PlainText.Should().Be("hi");
    }

    [Fact]
    public async Task SendAsync_NoDisplayName_UsesBareSenderAddress()
    {
        EmailMessage? captured = null;
        var client = new Mock<EmailClient>();
        client
            .Setup(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WaitUntil, EmailMessage, CancellationToken>((_, m, _) => captured = m)
            .ReturnsAsync((EmailSendOperation)null!);

        var sender = CreateSender(client, displayName: null);
        await sender.SendAsync("alice@example.com", "Subject", null, "body");

        captured!.SenderAddress.Should().Be("DoNotReply@managed.azurecomm.net");
    }

    [Fact]
    public async Task SendAsync_NotConfigured_IsNoOp_DoesNotThrowOrSend()
    {
        var client = new Mock<EmailClient>();
        // No endpoint/sender configured → the client is null and the sender is a safe no-op.
        var sender = new AcsEmailSender(
            client: null,
            options: Options.Create(new AcsEmailOptions()),
            logger: NullLogger<AcsEmailSender>.Instance);

        var act = async () => await sender.SendAsync("alice@example.com", "Subject", "<p>hi</p>", "hi");

        await act.Should().NotThrowAsync();
        client.Verify(
            c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_SubmitsWithWaitUntilStarted()
    {
        var client = new Mock<EmailClient>();
        client
            .Setup(c => c.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailSendOperation)null!);

        var sender = CreateSender(client);
        await sender.SendAsync("alice@example.com", "Subject", "<p>hi</p>", "hi");

        client.Verify(
            c => c.SendAsync(WaitUntil.Started, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
