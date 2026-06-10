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
// Service Bus. Nothing this Function does can roll the saga back. If this handler throws,
// only the side-effect (this one notification) is affected — the order remains confirmed.
//
// AUTHENTICATION
// --------------
// The producer (the Order service) publishes to the Event Grid topic using its Entra
// identity (no access key). This Function, in turn, authenticates to any Azure resource it
// needs via its own MANAGED IDENTITY — there are no secrets in configuration.
public class OrderConfirmedNotificationFunction
{
    private readonly ILogger<OrderConfirmedNotificationFunction> _logger;

    public OrderConfirmedNotificationFunction(ILogger<OrderConfirmedNotificationFunction> logger)
        => _logger = logger;

    [Function(nameof(OrderConfirmedNotificationFunction))]
    public void Run([EventGridTrigger] EventGridEvent input)
    {
        // For now the side-effect is RECORDED (logged). Actual email delivery is deferred to
        // the test-enablement step; when added, delivery happens here and still authenticates
        // through the managed identity — this method stays the single notification touch-point.
        _logger.LogInformation(
            "Notification side-effect received: {EventType} for {Subject}. Payload: {Data}",
            input.EventType, input.Subject, input.Data?.ToString());
    }
}
