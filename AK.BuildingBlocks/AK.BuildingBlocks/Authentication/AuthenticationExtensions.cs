using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AK.BuildingBlocks.Authentication;

// Shared JWT authentication setup used by every service (Products, Cart, Order, Payments, Notification).
// Calling AddKeycloakAuthentication() from a service's Program.cs wires up all of this automatically.
public static class AuthenticationExtensions
{
    // Registers JWT Bearer authentication backed by Keycloak as the identity provider.
    // Each service calls this once in Program.cs — no copy-pasting of JWT config per service.
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection("Keycloak").Get<KeycloakSettings>()
            ?? throw new InvalidOperationException("Keycloak settings are missing from configuration.");

        services.Configure<KeycloakSettings>(configuration.GetSection("Keycloak"));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority: Keycloak's OIDC discovery URL. The JWT middleware automatically
                // downloads the signing keys from {Authority}/.well-known/openid-configuration
                // and uses them to verify every incoming Bearer token's signature.
                options.Authority = settings.Authority;
                options.Audience = settings.Audience;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = false, // Keycloak uses azp claim; validated manually in OnTokenValidated
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ctx =>
                    {
                        var identity = ctx.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                        if (identity is null) return Task.CompletedTask;

                        // azp (authorized party) check: even if the token signature is valid,
                        // reject tokens that were issued for a different Keycloak client.
                        // This prevents a token from one microservice being reused against another.
                        var azp = ctx.Principal?.FindFirst("azp")?.Value;
                        if (!string.IsNullOrEmpty(settings.Audience) &&
                            !string.IsNullOrEmpty(azp) &&
                            azp != settings.Audience)
                        {
                            ctx.Fail($"Token azp '{azp}' does not match expected client '{settings.Audience}'.");
                            return Task.CompletedTask;
                        }

                        // Keycloak stores roles inside a JSON claim called "realm_access":
                        //   { "realm_access": { "roles": ["user", "admin"] } }
                        // ASP.NET Core's [Authorize(Roles="admin")] and RequireRole() look for
                        // ClaimTypes.Role claims, not this custom JSON claim. So we parse the JSON
                        // and add each role as a standard ClaimTypes.Role claim.
                        var realmAccess = ctx.Principal?.FindFirst("realm_access")?.Value;
                        if (realmAccess is null) return Task.CompletedTask;

                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(realmAccess);
                            if (doc.RootElement.TryGetProperty("roles", out var rolesEl))
                            {
                                foreach (var role in rolesEl.EnumerateArray())
                                {
                                    var roleValue = role.GetString();
                                    if (!string.IsNullOrEmpty(roleValue))
                                        identity.AddClaim(new System.Security.Claims.Claim(
                                            System.Security.Claims.ClaimTypes.Role, roleValue));
                                }
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            // Non-fatal: log a warning but don't reject the token.
                            // The user will simply have no roles attached.
                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<ILogger<JwtBearerEvents>>();
                            logger.LogWarning(ex, "Failed to parse realm_access claim");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        // Define named policies used in endpoint .RequireAuthorization("admin") calls.
        services.AddAuthorization(options =>
        {
            // "admin" policy: caller must have the "admin" role in their JWT realm_access.roles.
            options.AddPolicy("admin", policy =>
                policy.RequireRole("admin"));

            // "authenticated" policy: caller just needs a valid JWT, any role is fine.
            options.AddPolicy("authenticated", policy =>
                policy.RequireAuthenticatedUser());
        });

        return services;
    }

    // Registers both UseAuthentication() and UseAuthorization() in the correct order.
    // Authentication must come before Authorization — calling them in the wrong order causes
    // authorization policies to always fail because the user identity hasn't been set yet.
    public static WebApplication UseKeycloakAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
