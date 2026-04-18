using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace AK.Discount.Grpc.Interceptors;

public sealed class AuthInterceptor(ILogger<AuthInterceptor> logger) : Interceptor
{
    private static readonly HashSet<string> WriteMethodNames =
    [
        "/discount.DiscountProtoService/CreateDiscount",
        "/discount.DiscountProtoService/UpdateDiscount",
        "/discount.DiscountProtoService/DeleteDiscount"
    ];

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (WriteMethodNames.Contains(context.Method))
        {
            var authHeader = context.RequestHeaders.Get("authorization")?.Value;
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid authorization header."));

            var token = authHeader["Bearer ".Length..];
            if (!ValidateTokenAndHasAdminRole(token))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin role required."));
        }

        return await continuation(request, context);
    }

    private bool ValidateTokenAndHasAdminRole(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            var realmAccess = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
            if (string.IsNullOrEmpty(realmAccess)) return false;

            using var doc = System.Text.Json.JsonDocument.Parse(realmAccess);
            if (!doc.RootElement.TryGetProperty("roles", out var roles)) return false;

            foreach (var role in roles.EnumerateArray())
            {
                if (role.GetString() == "admin") return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token validation failed");
        }

        return false;
    }
}
