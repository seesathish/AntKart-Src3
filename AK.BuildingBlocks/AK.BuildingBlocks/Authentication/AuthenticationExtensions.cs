using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AK.BuildingBlocks.Authentication;

public static class AuthenticationExtensions
{
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

                        // Validate azp (authorized party) — ensures token was issued for this client
                        var azp = ctx.Principal?.FindFirst("azp")?.Value;
                        if (!string.IsNullOrEmpty(settings.Audience) &&
                            !string.IsNullOrEmpty(azp) &&
                            azp != settings.Audience)
                        {
                            ctx.Fail($"Token azp '{azp}' does not match expected client '{settings.Audience}'.");
                            return Task.CompletedTask;
                        }

                        // Map Keycloak realm_access.roles → ClaimTypes.Role
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
                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<ILogger<JwtBearerEvents>>();
                            logger.LogWarning(ex, "Failed to parse realm_access claim");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("admin", policy =>
                policy.RequireRole("admin"));

            options.AddPolicy("authenticated", policy =>
                policy.RequireAuthenticatedUser());
        });

        return services;
    }

    public static WebApplication UseKeycloakAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
