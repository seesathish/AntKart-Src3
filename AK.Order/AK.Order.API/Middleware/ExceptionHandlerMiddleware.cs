using FluentValidation;
using System.Net;
using System.Text.Json;

namespace AK.Order.API.Middleware;

// Global exception handler: sits at the top of the request pipeline and converts
// domain/application exceptions into appropriate HTTP responses.
//
// This keeps error handling in one place — handlers and domain entities just throw
// standard exceptions; they don't need to know about HTTP status codes.
//
// Exception → HTTP status mapping:
//   ValidationException (FluentValidation)  → 400 Bad Request  (invalid input)
//   UnauthorizedAccessException             → 403 Forbidden    (IDOR / ownership check failed)
//   KeyNotFoundException                    → 404 Not Found    (entity doesn't exist)
//   InvalidOperationException               → 409 Conflict     (business rule violation, e.g. bad state transition)
//   Any other Exception                     → 500 Internal Server Error
public sealed class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("Validation error: {Errors}", ex.Errors);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            var errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { errors }));
        }
        catch (UnauthorizedAccessException ex)
        {
            // Thrown by HttpContextExtensions.GetUserId() when the JWT has no 'sub' claim,
            // or by ownership checks in OrderEndpoints when a user tries to access another user's order.
            logger.LogWarning("Forbidden: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning("Not found: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        catch (InvalidOperationException ex)
        {
            // Used for business rule violations such as invalid order status transitions
            // or cancelling a delivered order.
            logger.LogWarning("Business rule violation: {Message}", ex.Message);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }));
        }
        catch (Exception ex)
        {
            // Catch-all: log the full exception (including stack trace) at Error level,
            // but only return a generic message to the caller to avoid leaking internals.
            logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "An unexpected error occurred." }));
        }
    }
}
