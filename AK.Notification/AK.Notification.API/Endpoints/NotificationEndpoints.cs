using AK.BuildingBlocks.Authentication;
using AK.Notification.Application.Queries;
using MediatR;

namespace AK.Notification.API.Endpoints;

// Notification endpoints follow the same IDOR-safe pattern as Orders and Payments:
// userId is always derived from the JWT via GetUserId(), never from a URL parameter.
// A regular user can only see their own notifications; admins can bypass the ownership check.
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization("authenticated");

        // GET /api/notifications/ — returns the current user's notification history (paged).
        // userId injected from JWT, so users can only list their own notifications.
        group.MapGet("/", async (
            HttpContext http,
            IMediator mediator,
            int page = 1,
            int pageSize = 20) =>
        {
            var userId = http.GetUserId();
            var result = await mediator.Send(new GetUserNotificationsQuery(userId, page, pageSize));
            return Results.Ok(result);
        })
        .WithName("GetMyNotifications");

        // GET /api/notifications/{id} — ownership check: regular users can only fetch
        // their own notifications. Admins bypass the userId filter by querying all and
        // filtering in-memory (acceptable because admins use this rarely for debugging).
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IMediator mediator) =>
        {
            var userId = http.GetUserId();
            var isAdmin = http.User.IsInRole("admin");

            if (isAdmin)
            {
                var allResult = await mediator.Send(new GetAllNotificationsQuery(1, int.MaxValue));
                var found = allResult.Items.FirstOrDefault(n => n.Id == id);
                return found is null ? Results.NotFound() : Results.Ok(found);
            }

            // GetNotificationByIdQuery throws UnauthorizedAccessException if the notification
            // belongs to a different user — mapped to 403 here rather than in middleware
            // so the route can return a clean Forbid() result.
            try
            {
                var notification = await mediator.Send(new GetNotificationByIdQuery(id, userId));
                return notification is null ? Results.NotFound() : Results.Ok(notification);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        })
        .WithName("GetNotificationById");

        // GET /api/notifications/admin — paginated view of ALL notifications across all users.
        // Registered directly on app (not the group) because it needs a different auth policy.
        app.MapGet("/api/notifications/admin", async (IMediator mediator, int page = 1, int pageSize = 20) =>
        {
            var result = await mediator.Send(new GetAllNotificationsQuery(page, pageSize));
            return Results.Ok(result);
        })
        .WithTags("Notifications")
        .RequireAuthorization("admin")
        .WithName("GetAllNotifications");
    }
}
