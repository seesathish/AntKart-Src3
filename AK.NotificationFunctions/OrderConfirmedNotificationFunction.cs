using System.Text.Json;
using AK.BuildingBlocks.Email;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AK.NotificationFunctions;

// Event Grid-triggered notification side-effect handler (.NET 9 isolated worker).
//
// WHERE THIS SITS IN THE PLATFORM
// -------------------------------
// AntKart uses two deliberately separate eventing mechanisms:
//
//   1. Durable backbone  — Azure Service Bus + the order saga. This carries the
//      transaction-critical steps (reserve stock -> take payment -> confirm). It is
//      ordered, pull-based, and competing-consumer; messages are retried until handled.
//
//   2. Side-effects       — Azure Event Grid + THIS serverless Function. This carries
//      discrete, fire-and-forget reactions (notifications) that must NOT fail or delay
//      the core transaction. Event Grid PUSHES each event to this Function (the Function
//      is the registered handler). The app scales to zero and is billed per execution.
//
// DECOUPLING GUARANTEE
// --------------------
// By the time an event reaches this Function, the order saga has ALREADY committed over
// Service Bus. Nothing this Function does can roll the saga back. The actual email send is
// wrapped in try/catch: a delivery failure is logged and swallowed, so a flaky mail send can
// never fault the Function in a way that affects the order.
//
// AUTHENTICATION
// --------------
// The Function sends through IEmailSender (ACS Email), whose EmailClient is built from
// Acs:Endpoint + DefaultAzureCredential — i.e. the Function App's MANAGED IDENTITY. No keys.
public class OrderConfirmedNotificationFunction
{
    private readonly IEmailSender _emailSender;
    private readonly ILogger<OrderConfirmedNotificationFunction> _logger;

    public OrderConfirmedNotificationFunction(
        IEmailSender emailSender, ILogger<OrderConfirmedNotificationFunction> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    // The side-effect payload published by AK.Order's OrderConfirmedConsumer.
    private sealed record OrderConfirmedSideEffect(
        string? OrderId, string? OrderNumber, string? CustomerEmail, string? CustomerName);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Function(nameof(OrderConfirmedNotificationFunction))]
    public async Task Run([EventGridTrigger] EventGridEvent input)
    {
        var data = input.Data?.ToObjectFromJson<OrderConfirmedSideEffect>(JsonOptions);

        if (data is null || string.IsNullOrWhiteSpace(data.CustomerEmail))
        {
            _logger.LogWarning(
                "Order-confirmed side-effect {Subject} had no recipient; nothing to send.", input.Subject);
            return;
        }

        var name = string.IsNullOrWhiteSpace(data.CustomerName) ? "there" : data.CustomerName;
        var orderNumber = data.OrderNumber ?? "your order";
        var subject = $"Your AntKart order {orderNumber} is confirmed";
        var html =
            $"<p>Hi {name},</p>" +
            $"<p>Good news — your order <strong>{orderNumber}</strong> has been confirmed and is being prepared.</p>" +
            "<p>Thank you for shopping with AntKart.</p>";
        var plainText =
            $"Hi {name}, your order {orderNumber} has been confirmed and is being prepared. Thank you for shopping with AntKart.";

        try
        {
            await _emailSender.SendAsync(data.CustomerEmail, subject, html, plainText);
            _logger.LogInformation(
                "Order-confirmation email sent for {OrderNumber} to {Recipient}.", orderNumber, data.CustomerEmail);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: swallow so the side-effect failure cannot fault the Function or
            // affect the (already-committed) order. The event can be retried by Event Grid.
            _logger.LogError(ex,
                "Failed to send order-confirmation email for {OrderNumber}; the order is unaffected.", orderNumber);
        }
    }
}
