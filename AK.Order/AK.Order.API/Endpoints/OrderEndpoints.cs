using AK.BuildingBlocks.Authentication;
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

        // GET /api/orders — admin sees all (optional ?userId filter); regular users see only their own
        group.MapGet("/", async (
            HttpContext http,
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            string? userId = null,
            OrderStatus? status = null) =>
        {
            var isAdmin = http.User.IsInRole("admin");
            var effectiveUserId = isAdmin ? userId : http.GetUserId();
            var result = await mediator.Send(new GetOrdersQuery(page, pageSize, effectiveUserId, status));
            return Results.Ok(result);
        })
        .WithName("GetOrders");

        // GET /api/orders/{id} — owner or admin
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var order = await mediator.Send(new GetOrderByIdQuery(id));
            if (order is null) return Results.NotFound();

            var isAdmin = http.User.IsInRole("admin");
            if (!isAdmin && order.UserId != http.GetUserId())
                return Results.Forbid();

            return Results.Ok(order);
        })
        .WithName("GetOrderById");

        // GET /api/orders/me — current user's orders (paged)
        group.MapGet("/me", async (HttpContext http, IMediator mediator, int page = 1, int pageSize = 20) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new GetOrdersByUserQuery(userId, page, pageSize));
            return Results.Ok(result);
        })
        .WithName("GetMyOrders");

        // POST /api/orders — userId injected from JWT (not accepted from request body)
        group.MapPost("/", async (HttpContext http, CreateOrderDto orderDto, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var order = await mediator.Send(new CreateOrderCommand(userId, orderDto));
            return Results.Created($"/api/orders/{order.Id}", order);
        })
        .WithName("CreateOrder");

        // PUT /api/orders/{id}/status — admin only
        group.MapPut("/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest req, IMediator mediator) =>
        {
            var order = await mediator.Send(new UpdateOrderStatusCommand(id, req.NewStatus));
            return Results.Ok(order);
        })
        .RequireAuthorization("admin")
        .WithName("UpdateOrderStatus");

        // DELETE /api/orders/{id} — owner or admin
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var order = await mediator.Send(new GetOrderByIdQuery(id));
            if (order is null) return Results.NotFound();

            var isAdmin = http.User.IsInRole("admin");
            if (!isAdmin && order.UserId != http.GetUserId())
                return Results.Forbid();

            await mediator.Send(new CancelOrderCommand(id));
            return Results.NoContent();
        })
        .WithName("CancelOrder");
    }
}

public sealed record UpdateOrderStatusRequest(OrderStatus NewStatus);
