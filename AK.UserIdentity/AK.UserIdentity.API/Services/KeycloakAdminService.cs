using AK.BuildingBlocks.Authentication;
using AK.UserIdentity.API.DTOs;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AK.UserIdentity.API.Services;

public sealed class KeycloakAdminService(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakSettings> settings) : IKeycloakAdminService
{
    private readonly KeycloakSettings _settings = settings.Value;

    public async Task<string> GetAdminTokenAsync(CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        var tokenUrl = $"{_settings.AdminUrl}/realms/{_settings.Realm}/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret
        });

        var response = await client.PostAsync(tokenUrl, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public async Task<List<KeycloakUserSummary>> GetUsersAsync(string adminToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        var usersUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/users";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync(usersUrl, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Failed to retrieve users.");

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.EnumerateArray().Select(u => new KeycloakUserSummary(
            u.TryGetProperty("id", out var id) ? id.GetString()! : string.Empty,
            u.TryGetProperty("username", out var un) ? un.GetString()! : string.Empty,
            u.TryGetProperty("email", out var em) ? em.GetString()! : string.Empty,
            u.TryGetProperty("firstName", out var fn) ? fn.GetString()! : string.Empty,
            u.TryGetProperty("lastName", out var ln) ? ln.GetString()! : string.Empty,
            u.TryGetProperty("enabled", out var en) && en.GetBoolean()
        )).ToList();
    }

    public async Task AssignRoleAsync(string userId, string role, string adminToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var rolesUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/roles/{role}";
        var roleResponse = await client.GetAsync(rolesUrl, ct);
        if (!roleResponse.IsSuccessStatusCode)
            throw new KeyNotFoundException($"Role '{role}' not found.");

        var roleJson = await roleResponse.Content.ReadAsStringAsync(ct);
        var assignUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/users/{userId}/role-mappings/realm";
        var assignContent = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(assignUrl, assignContent, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to assign role '{role}' to user '{userId}'.");
    }
}
