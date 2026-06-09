namespace AK.BuildingBlocks.Authentication;

// Microsoft Entra ID settings used to validate incoming JWT access tokens.
// Every value here is NON-SECRET (tenant id, instance, audience) and is committed in
// appsettings — validating a token needs only public metadata, never a credential.
public sealed class EntraSettings
{
    // The Entra (Microsoft identity platform) cloud instance.
    public string Instance { get; init; } = "https://login.microsoftonline.com";

    // The directory (tenant) id that issues the tokens this API trusts.
    public string TenantId { get; init; } = string.Empty;

    // The API app registration this service validates tokens for — its Application ID URI
    // (api://<name>) or client id. Tokens carry this as their `aud` claim.
    public string Audience { get; init; } = string.Empty;

    // Optional explicit authority override. When empty it is derived as
    // {Instance}/{TenantId}/v2.0 (the Entra v2 endpoint).
    public string Authority { get; init; } = string.Empty;

    // Entra metadata is served over HTTPS, so this stays true in every environment.
    public bool RequireHttpsMetadata { get; init; } = true;

    // The OIDC authority the JWT middleware downloads metadata + signing keys from.
    public string ResolveAuthority() =>
        string.IsNullOrWhiteSpace(Authority)
            ? $"{Instance.TrimEnd('/')}/{TenantId}/v2.0"
            : Authority;

    // The exact issuer (`iss`) an Entra v2 token carries for this tenant.
    public string ResolveIssuer() =>
        $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";
}
