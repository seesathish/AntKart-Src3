using AK.Payments.Application.Commands.InitiatePayment;
using AK.Payments.Application.Commands.VerifyPayment;
using AK.Payments.Application.Queries.GetPaymentById;
using AK.Payments.Application.Queries.GetPaymentByOrderId;
using AK.Payments.Application.Queries.GetUserPayments;
using MediatR;

namespace AK.Payments.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/payments")
            .WithTags("Payments")
            .RequireAuthorization("authenticated");

        group.MapPost("/initiate", async (InitiatePaymentCommand command, IMediator mediator) =>
        {
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

        group.MapGet("/user/{userId}", async (string userId, IMediator mediator) =>
        {
            var payments = await mediator.Send(new GetUserPaymentsQuery(userId));
            return Results.Ok(payments);
        }).WithName("GetUserPayments");

        app.MapPost("/api/payments/webhook", () => Results.Ok())
            .WithName("PaymentWebhook")
            .WithTags("Payments")
            .AllowAnonymous();
    }
}
