using AK.UserIdentity.API.DTOs;
using AK.UserIdentity.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace AK.UserIdentity.API.Endpoints;

// Admin-only endpoints that proxy through to Keycloak's Admin REST API.
// Every request obtains a fresh short-lived service-account token (client_credentials)
// before calling Keycloak — we don't cache it here because the token lifetime is short
// and caching adds complexity. The "admin" authorization policy (set in AuthenticationExtensions)
// requires the caller's JWT to contain the "admin" realm role from Keycloak.
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin")
            .RequireAuthorization("admin");

        // GET /api/admin/users — returns all Keycloak users in the realm.
        // GetAdminTokenAsync uses the service account (client_credentials) to obtain
        // a token with the realm-admin role so we can read the user list.
        group.MapGet("/users", async (IKeycloakAdminService adminSvc, CancellationToken ct) =>
        {
            var adminToken = await adminSvc.GetAdminTokenAsync(ct);
            var users = await adminSvc.GetUsersAsync(adminToken, ct);
            return Results.Ok(users);
        })
        .WithName("GetAllUsers")
        .WithSummary("Get all registered users (Admin only)");

        // POST /api/admin/users/{id}/roles — assigns a Keycloak realm role to a user.
        // Flow: fetch role object by name → POST it to the user's role-mappings endpoint.
        // Two HTTP calls to Keycloak are required because the assign API needs the full
        // role representation (id + name), not just the role name string.
        group.MapPost("/users/{id}/roles", async (
            string id,
            [FromBody] AssignRoleRequest req,
            IKeycloakAdminService adminSvc,
            CancellationToken ct) =>
        {
            var adminToken = await adminSvc.GetAdminTokenAsync(ct);
            await adminSvc.AssignRoleAsync(id, req.Role, adminToken, ct);
            return Results.Ok(new { message = $"Role '{req.Role}' assigned to user '{id}'." });
        })
        .WithName("AssignRole")
        .WithSummary("Assign a role to a user (Admin only)");
    }
}
