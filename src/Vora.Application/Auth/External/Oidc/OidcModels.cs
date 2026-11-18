namespace Vora.Application.Auth.External.Oidc;

public sealed record OidcDiscovery(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string JwksUri,
    string? UserInfoEndpoint,
    bool SupportsS256,
    bool AdvertisesS256,
    bool PrefersPostAuthentication);

public sealed record OidcTokenResponse(string IdToken, string? AccessToken);

public sealed record OidcLoginState(
    Guid ProviderId,
    string Nonce,
    string CodeVerifier,
    string RedirectUri,
    string? ReturnTo,
    string? InviteToken,
    Guid? LinkAccountId);

public sealed record ExternalPending(
    Guid ProviderId,
    string ProviderName,
    ExternalClaims Claims,
    string? InviteToken,
    string? ReturnTo,
    ExternalSignInResult Last,
    int Failures);

public sealed class OidcProviderException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class OidcErrorCodes
{
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderError = "provider_error";
    public const string AccessDenied = "access_denied";
    public const string StateExpired = "state_expired";
    public const string DiscoveryFailed = "discovery_failed";
    public const string TokenExchangeFailed = "token_exchange_failed";
    public const string IdTokenInvalid = "id_token_invalid";
    public const string MissingSubject = "missing_subject";
    public const string LinkConflict = "link_conflict";
    public const string LinkRefused = "link_refused";
}
