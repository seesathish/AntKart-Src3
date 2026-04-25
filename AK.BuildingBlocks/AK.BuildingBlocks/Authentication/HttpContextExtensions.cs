using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AK.BuildingBlocks.Authentication;

// Helper methods for reading user identity from the JWT inside an endpoint or handler.
//
// Security rule: user-scoped operations (get my cart, create my order) must ALWAYS derive
// the userId from the JWT via GetUserId(), never from a URL parameter or request body.
// This prevents IDOR (Insecure Direct Object Reference) attacks where a logged-in user
// could access or modify another user's data simply by changing a userId in the URL.
public static class HttpContextExtensions
{
    // Returns the caller's stable user UUID from the 'sub' (subject) claim.
    // Keycloak JWT: 'sub' is the UUID that never changes even if the username changes.
    // Falls back to 'preferred_username' for non-Keycloak tokens (e.g. test environments).
    // Throws UnauthorizedAccessException (→ HTTP 403) if no identity claim is present,
    // which means the JWT is malformed or the user is not authenticated.
    public static string GetUserId(this HttpContext ctx) =>
        ctx.User.FindFirst("sub")?.Value
        ?? ctx.User.FindFirst("preferred_username")?.Value
        ?? throw new UnauthorizedAccessException("User identity could not be determined from token.");

    // Returns the caller's email from the JWT 'email' claim.
    // Returns empty string (not null) if missing — email is optional for some operations.
    public static string GetUserEmail(this HttpContext ctx) =>
        ctx.User.FindFirst("email")?.Value
        ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value
        ?? string.Empty;

    // Returns a human-readable display name, trying several Keycloak claims in order.
    // Used when denormalising customer name into Order and Payment records so they don't
    // need to call the UserIdentity service to look up a name later.
    public static string GetUserDisplayName(this HttpContext ctx) =>
        ctx.User.FindFirst("name")?.Value
        ?? $"{ctx.User.FindFirst("given_name")?.Value} {ctx.User.FindFirst("family_name")?.Value}".Trim()
        ?? ctx.User.FindFirst("preferred_username")?.Value
        ?? "Customer";
}
