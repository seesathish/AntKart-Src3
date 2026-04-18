using AK.Order.Application.Common.DTOs;
using AK.Order.Application.Features.CancelOrder;
using AK.Order.Application.Features.CreateOrder;
using AK.Order.Application.Features.GetOrderById;
using AK.Order.Application.Features.GetOrders;
using AK.Order.Application.Features.GetOrdersByUser;
using AK.Order.Application.Features.UpdateOrderStatus;
using AK.Order.Domain.Enums;
using MediatR;

namespace AK.Order.API.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization("authenticated");

        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            string? userId = null,
            OrderStatus? status = null) =>
        {
            var result = await mediator.Send(new GetOrdersQuery(page, pageSize, userId, status));
            return Results.Ok(result);
        })
        .WithName("GetOrders");

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var order = await mediator.Send(new GetOrderByIdQuery(id));
            return order is null ? Results.NotFound() : Results.Ok(order);
        })
        .WithName("GetOrderById");

        group.MapGet("/user/{userId}", async (string userId, IMediator mediator, int page = 1, int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetOrdersByUserQuery(userId, page, pageSize));
            return Results.Ok(result);
        })
        .WithName("GetOrdersByUser");

        group.MapPost("/", async (CreateOrderCommand command, IMediator mediator) =>
        {
            var order = await mediator.Send(command);
            return Results.Created($"/api/orders/{order.Id}", order);
        })
        .WithName("CreateOrder");

        group.MapPut("/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest req, IMediator mediator) =>
        {
            var order = await mediator.Send(new UpdateOrderStatusCommand(id, req.NewStatus));
            return Results.Ok(order);
        })
        .WithName("UpdateOrderStatus");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            await mediator.Send(new CancelOrderCommand(id));
            return Results.NoContent();
        })
        .WithName("CancelOrder");
    }
}

public sealed record UpdateOrderStatusRequest(OrderStatus NewStatus);
