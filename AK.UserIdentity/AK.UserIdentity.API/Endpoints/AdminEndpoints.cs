using AK.UserIdentity.API.DTOs;
using AK.UserIdentity.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace AK.UserIdentity.API.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin")
            .RequireAuthorization("admin");

        // GET /api/admin/users
        group.MapGet("/users", async (IKeycloakAdminService adminSvc, CancellationToken ct) =>
        {
            var adminToken = await adminSvc.GetAdminTokenAsync(ct);
            var users = await adminSvc.GetUsersAsync(adminToken, ct);
            return Results.Ok(users);
        })
        .WithName("GetAllUsers")
        .WithSummary("Get all registered users (Admin only)");

        // POST /api/admin/users/{id}/roles
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
