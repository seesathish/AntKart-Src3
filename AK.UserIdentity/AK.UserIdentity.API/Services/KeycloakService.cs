using AK.BuildingBlocks.Authentication;
using AK.UserIdentity.API.DTOs;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AK.UserIdentity.API.Services;

public sealed class KeycloakService(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakSettings> settings) : IKeycloakService
{
    private readonly KeycloakSettings _settings = settings.Value;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<TokenResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        var tokenUrl = $"{_settings.AdminUrl}/realms/{_settings.Realm}/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid"
        });

        var response = await client.PostAsync(tokenUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new UnauthorizedAccessException($"Login failed: {error}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new TokenResponse(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.GetProperty("token_type").GetString()!);
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var adminToken = await GetServiceAccountTokenAsync(ct);
        var client = httpClientFactory.CreateClient("keycloak");
        var createUserUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/users";

        var userPayload = new
        {
            username = request.Username,
            email = request.Email,
            firstName = request.FirstName,
            lastName = request.LastName,
            enabled = true,
            credentials = new[]
            {
                new { type = "password", value = request.Password, temporary = false }
            }
        };

        var json = JsonSerializer.Serialize(userPayload);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsync(createUserUrl, httpContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                throw new InvalidOperationException("User already exists.");
            throw new InvalidOperationException($"Registration failed: {error}");
        }

        var locationHeader = response.Headers.Location?.ToString();
        if (locationHeader is not null)
        {
            var userId = locationHeader.Split('/').Last();
            await AssignDefaultRoleAsync(userId, adminToken, client, ct);
        }
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        var tokenUrl = $"{_settings.AdminUrl}/realms/{_settings.Realm}/protocol/openid-connect/token";

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["refresh_token"] = refreshToken
        });

        var response = await client.PostAsync(tokenUrl, content, ct);

        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException("Token refresh failed.");

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new TokenResponse(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.GetProperty("token_type").GetString()!);
    }

    public async Task<UserInfoResponse> GetUserInfoAsync(string accessToken, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("keycloak");
        var userInfoUrl = $"{_settings.AdminUrl}/realms/{_settings.Realm}/protocol/openid-connect/userinfo";

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.GetAsync(userInfoUrl, ct);

        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException("Invalid token.");

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var roles = new List<string>();
        if (root.TryGetProperty("realm_access", out var realmAccess) &&
            realmAccess.TryGetProperty("roles", out var rolesEl))
        {
            roles.AddRange(rolesEl.EnumerateArray()
                .Select(r => r.GetString())
                .Where(r => r is not null)
                .Cast<string>());
        }

        return new UserInfoResponse(
            root.TryGetProperty("sub", out var sub) ? sub.GetString()! : string.Empty,
            root.TryGetProperty("preferred_username", out var un) ? un.GetString()! : string.Empty,
            root.TryGetProperty("email", out var em) ? em.GetString()! : string.Empty,
            root.TryGetProperty("given_name", out var fn) ? fn.GetString()! : string.Empty,
            root.TryGetProperty("family_name", out var ln) ? ln.GetString()! : string.Empty,
            roles);
    }

    private async Task<string> GetServiceAccountTokenAsync(CancellationToken ct)
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

    private async Task AssignDefaultRoleAsync(string userId, string adminToken, HttpClient client, CancellationToken ct)
    {
        var rolesUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/roles/user";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var roleResponse = await client.GetAsync(rolesUrl, ct);
        if (!roleResponse.IsSuccessStatusCode) return;

        var roleJson = await roleResponse.Content.ReadAsStringAsync(ct);
        var assignUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/users/{userId}/role-mappings/realm";
        var assignContent = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json");
        await client.PostAsync(assignUrl, assignContent, ct);
    }
}
