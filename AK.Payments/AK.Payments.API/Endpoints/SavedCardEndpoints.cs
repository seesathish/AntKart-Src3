using AK.Payments.Application.Commands.DeleteSavedCard;
using AK.Payments.Application.Commands.SaveCard;
using AK.Payments.Application.Queries.GetUserSavedCards;
using MediatR;

namespace AK.Payments.API.Endpoints;

public static class SavedCardEndpoints
{
    public static void MapSavedCardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/payments/cards")
            .WithTags("SavedCards")
            .RequireAuthorization("authenticated");

        group.MapGet("/user/{userId}", async (string userId, IMediator mediator) =>
        {
            var cards = await mediator.Send(new GetUserSavedCardsQuery(userId));
            return Results.Ok(cards);
        }).WithName("GetUserSavedCards");

        group.MapPost("/save", async (SaveCardCommand command, IMediator mediator) =>
        {
            var card = await mediator.Send(command);
            return Results.Created($"/api/payments/cards/{card.Id}", card);
        }).WithName("SaveCard");

        group.MapDelete("/{id:guid}", async (Guid id, string userId, IMediator mediator) =>
        {
            await mediator.Send(new DeleteSavedCardCommand(id, userId));
            return Results.NoContent();
        }).WithName("DeleteSavedCard");
    }
}
