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
        // All order endpoints require at least a valid JWT (authenticated policy).
        // Individual endpoints below may require the "admin" policy for elevated operations.
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

        // GET /api/orders/me — current user's orders (paged).
        // Registered before /{id:guid} so the literal "me" segment is matched first.
        // The :guid constraint would also prevent "me" from matching /{id:guid}, but
        // explicit ordering makes intent clear and is safer if the constraint is ever loosened.
        group.MapGet("/me", async (HttpContext http, IMediator mediator, int page = 1, int pageSize = 20) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new GetOrdersByUserQuery(userId, page, pageSize));
            return Results.Ok(result);
        })
        .WithName("GetMyOrders");

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

        // POST /api/orders — userId, email, and name are extracted from the JWT, NOT the request body.
        // This prevents IDOR: a malicious client cannot create an order under a different user's ID
        // by supplying a different userId in the JSON body.
        group.MapPost("/", async (HttpContext http, CreateOrderDto orderDto, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var customerEmail = http.GetUserEmail();
            var customerName = http.GetUserDisplayName();
            var order = await mediator.Send(new CreateOrderCommand(userId, customerEmail, customerName, orderDto));
            return Results.Created($"/api/orders/{order.Id}", order);
        })
        .WithName("CreateOrder");

        // PUT /api/orders/{id}/status — admin only
        // Status transitions are admin-controlled (e.g. marking an order as Shipped).
        // Handler returns Result<OrderDto>: Failure maps to 409 with the domain's error message.
        group.MapPut("/{id:guid}/status", async (Guid id, UpdateOrderStatusRequest req, IMediator mediator) =>
        {
            var result = await mediator.Send(new UpdateOrderStatusCommand(id, req.NewStatus));
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Conflict(new { error = result.Error });
        })
        .RequireAuthorization("admin")
        .WithName("UpdateOrderStatus");

        // DELETE /api/orders/{id} — owner or admin
        // Handler returns Result<bool>: Failure maps to 409 with the domain's error message.
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var order = await mediator.Send(new GetOrderByIdQuery(id));
            if (order is null) return Results.NotFound();

            var isAdmin = http.User.IsInRole("admin");
            if (!isAdmin && order.UserId != http.GetUserId())
                return Results.Forbid();

            var result = await mediator.Send(new CancelOrderCommand(id));
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Conflict(new { error = result.Error });
        })
        .WithName("CancelOrder");
    }
}

public sealed record UpdateOrderStatusRequest(OrderStatus NewStatus);
