using AK.ShoppingCart.Application.Commands.AddToCart;
using AK.ShoppingCart.Application.Commands.ClearCart;
using AK.ShoppingCart.Application.Commands.RemoveFromCart;
using AK.ShoppingCart.Application.Commands.UpdateCartItem;
using AK.ShoppingCart.Application.DTOs;
using AK.ShoppingCart.Application.Queries.GetCart;
using MediatR;

namespace AK.ShoppingCart.API.Endpoints;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/cart")
            .WithTags("Cart");

        // GET /api/v1/cart/{userId}
        group.MapGet("/{userId}", async (string userId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetCartQuery(userId), ct);
            return result is null
                ? Results.NotFound(new { error = $"Cart for user '{userId}' not found" })
                : Results.Ok(result);
        })
        .WithName("GetCart")
        .WithSummary("Get cart for a user");

        // POST /api/v1/cart/{userId}/items
        group.MapPost("/{userId}/items", async (string userId, AddCartItemDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new AddToCartCommand(userId, dto), ct);
            return Results.Ok(result);
        })
        .WithName("AddToCart")
        .WithSummary("Add an item to the cart");

        // PUT /api/v1/cart/{userId}/items/{productId}
        group.MapPut("/{userId}/items/{productId}", async (string userId, string productId, UpdateCartItemDto dto, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateCartItemCommand(userId, productId, dto.Quantity), ct);
            return Results.Ok(result);
        })
        .WithName("UpdateCartItem")
        .WithSummary("Update quantity of a cart item (0 removes the item)");

        // DELETE /api/v1/cart/{userId}/items/{productId}
        group.MapDelete("/{userId}/items/{productId}", async (string userId, string productId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RemoveFromCartCommand(userId, productId), ct);
            return Results.Ok(result);
        })
        .WithName("RemoveFromCart")
        .WithSummary("Remove an item from the cart");

        // DELETE /api/v1/cart/{userId}
        group.MapDelete("/{userId}", async (string userId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new ClearCartCommand(userId), ct);
            return Results.NoContent();
        })
        .WithName("ClearCart")
        .WithSummary("Clear all items from the cart");
    }
}
