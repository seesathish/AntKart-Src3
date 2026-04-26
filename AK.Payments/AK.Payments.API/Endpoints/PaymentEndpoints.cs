using AK.BuildingBlocks.Authentication;
using AK.Payments.Application.Commands.InitiatePayment;
using AK.Payments.Application.Commands.VerifyPayment;
using AK.Payments.Application.Queries.GetPaymentById;
using AK.Payments.Application.Queries.GetPaymentByOrderId;
using AK.Payments.Application.Queries.GetUserPayments;
using AK.Payments.Domain.Enums;
using MediatR;

namespace AK.Payments.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        // All payment endpoints require a valid JWT. Razorpay amounts are in INR (paise conversion
        // happens inside RazorpayGatewayClient). The two-step flow is: initiate → verify.
        var group = app.MapGroup("/api/payments")
            .WithTags("Payments")
            .RequireAuthorization("authenticated");

        // POST /api/payments/initiate — userId, email, name injected from JWT (not from request body)
        group.MapPost("/initiate", async (HttpContext http, InitiatePaymentRequest req, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var customerEmail = http.GetUserEmail();
            var customerName = http.GetUserDisplayName();
            var command = new InitiatePaymentCommand(
                req.OrderId, userId, customerEmail, customerName, req.OrderNumber,
                req.Amount, req.Method, req.SavedCardToken, req.CustomerContact);
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }).WithName("InitiatePayment");

        group.MapPost("/verify", async (VerifyPaymentCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            return Results.Ok(result);
        }).WithName("VerifyPayment");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var payment = await mediator.Send(new GetPaymentByIdQuery(id));
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        }).WithName("GetPaymentById");

        group.MapGet("/order/{orderId:guid}", async (Guid orderId, IMediator mediator) =>
        {
            var payment = await mediator.Send(new GetPaymentByOrderIdQuery(orderId));
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        }).WithName("GetPaymentByOrderId");

        // GET /api/payments/me — current user's payment history
        group.MapGet("/me", async (HttpContext http, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var payments = await mediator.Send(new GetUserPaymentsQuery(userId));
            return Results.Ok(payments);
        }).WithName("GetMyPayments");

    }
}

// userId, customerEmail, customerName are always derived from the JWT — not accepted from client
public sealed record InitiatePaymentRequest(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    PaymentMethod Method,
    string? SavedCardToken = null,
    string? CustomerContact = null);
