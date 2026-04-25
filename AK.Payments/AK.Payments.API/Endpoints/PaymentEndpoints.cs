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
        var group = app.MapGroup("/api/payments")
            .WithTags("Payments")
            .RequireAuthorization("authenticated");

        // POST /api/payments/initiate — userId injected from JWT
        group.MapPost("/initiate", async (HttpContext http, InitiatePaymentRequest req, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var command = new InitiatePaymentCommand(
                req.OrderId, userId, req.Amount, req.Method,
                req.SavedCardToken, req.CustomerEmail, req.CustomerContact);
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

        app.MapPost("/api/payments/webhook", () => Results.Ok())
            .WithName("PaymentWebhook")
            .WithTags("Payments")
            .AllowAnonymous();
    }
}

// userId is not accepted from the client — it is always derived from the JWT
public sealed record InitiatePaymentRequest(
    Guid OrderId,
    decimal Amount,
    PaymentMethod Method,
    string? SavedCardToken = null,
    string? CustomerEmail = null,
    string? CustomerContact = null);
