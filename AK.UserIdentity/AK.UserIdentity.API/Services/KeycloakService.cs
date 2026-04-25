using AK.BuildingBlocks.Authentication;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.UserIdentity.API.DTOs;
using MassTransit;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AK.UserIdentity.API.Services;

// Thin proxy to Keycloak's OpenID Connect API.
// All Keycloak HTTP calls go through this service — no other code talks to Keycloak directly.
// Uses IHttpClientFactory to get named "keycloak" HttpClients (with resilience pipeline applied).
public sealed class KeycloakService(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakSettings> settings,
    IPublishEndpoint publisher) : IKeycloakService
{
    private readonly KeycloakSettings _settings = settings.Value;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Resource Owner Password Credentials (ROPC) grant — exchanges username/password for JWT tokens.
    // Only safe to use in server-side code (the credentials are sent to our server, not Keycloak directly).
    // Returns access_token, refresh_token, and expiry.
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
            ["scope"] = "openid"   // requests an ID token in addition to the access token
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

    // Registration flow:
    //   1. Get a service account token (client_credentials grant — no user required)
    //   2. Use that token to call Keycloak Admin REST API to create the user
    //   3. Extract the new user's UUID from the Location response header
    //   4. Assign the default "user" realm role to the new account
    //   5. Publish UserRegisteredIntegrationEvent → AK.Notification sends welcome email
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
            await publisher.Publish(new UserRegisteredIntegrationEvent(
                userId,
                request.Email,
                $"{request.FirstName} {request.LastName}".Trim()), ct);
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

    // Client Credentials grant — gets a token for the service account of our Keycloak client.
    // The service account ("service-account-antkart-client") must have the manage-users and
    // view-users realm-management roles granted in Keycloak (configured in antkart-realm.json).
    // This token is used for admin operations (create user, assign role) without requiring
    // a logged-in admin user to be present.
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

    // Assigns the "user" realm role to a newly registered user.
    // Keycloak role assignment requires two calls:
    //   1. GET /roles/user          → fetch the role object (contains id + name)
    //   2. POST /users/{id}/role-mappings/realm  → assign the role using the full role object
    // We wrap the fetched role JSON in an array because the assign endpoint expects a list.
    private async Task AssignDefaultRoleAsync(string userId, string adminToken, HttpClient client, CancellationToken ct)
    {
        var rolesUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/roles/user";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var roleResponse = await client.GetAsync(rolesUrl, ct);
        if (!roleResponse.IsSuccessStatusCode) return;

        var roleJson = await roleResponse.Content.ReadAsStringAsync(ct);

        // Wrap in array: Keycloak role-mappings endpoint expects [ { "id": "...", "name": "user" } ]
        var assignUrl = $"{_settings.AdminUrl}/admin/realms/{_settings.Realm}/users/{userId}/role-mappings/realm";
        var assignContent = new StringContent($"[{roleJson}]", Encoding.UTF8, "application/json");
        await client.PostAsync(assignUrl, assignContent, ct);
    }
}
