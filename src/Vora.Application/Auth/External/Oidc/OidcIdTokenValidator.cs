using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Auth.External.Oidc;

public interface IOidcIdTokenValidator
{
    Task<IReadOnlyDictionary<string, object>> ValidateAsync(AuthProvider provider, OidcDiscovery discovery, string idToken, string expectedNonce, CancellationToken cancellationToken);
}

public sealed class OidcIdTokenValidator(IOidcProviderClient client, TimeProvider clock, ILogger<OidcIdTokenValidator> logger) : IOidcIdTokenValidator
{
    private static readonly string[] Algorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512
    ];

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public async Task<IReadOnlyDictionary<string, object>> ValidateAsync(AuthProvider provider, OidcDiscovery discovery, string idToken, string expectedNonce, CancellationToken cancellationToken)
    {
        var handler = new JsonWebTokenHandler();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var keys = await client.GetSigningKeysAsync(discovery, refresh: attempt == 1, cancellationToken);
            var result = await handler.ValidateTokenAsync(idToken, Parameters(provider, keys));
            if (result.IsValid)
            {
                return CheckClaims(provider, result.Claims, expectedNonce);
            }

            if (attempt == 0 && result.Exception is SecurityTokenInvalidSignatureException)
            {
                continue;
            }

            logger.LogWarning("An id token from the provider {Provider} was refused: {Reason}", provider.Slug, Describe(result.Exception));
            throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, Describe(result.Exception));
        }

        throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "The id token's signature does not match the provider's keys.");
    }

    private TokenValidationParameters Parameters(AuthProvider provider, IReadOnlyList<SecurityKey> keys) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = provider.Issuer,
        ValidateAudience = true,
        ValidAudience = provider.ClientId,
        ValidateLifetime = true,
        LifetimeValidator = ValidateLifetime,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = keys,
        ValidAlgorithms = Algorithms,
        ClockSkew = ClockSkew
    };

    private bool ValidateLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (expires is null || expires.Value.ToUniversalTime() + ClockSkew <= now)
        {
            throw new SecurityTokenExpiredException("The id token has expired.");
        }

        if (notBefore is not null && notBefore.Value.ToUniversalTime() - ClockSkew > now)
        {
            throw new SecurityTokenNotYetValidException("The id token is not valid yet.");
        }

        return true;
    }

    private IReadOnlyDictionary<string, object> CheckClaims(AuthProvider provider, IDictionary<string, object> claims, string expectedNonce)
    {
        if (!claims.TryGetValue("nonce", out var nonce) || !string.Equals(nonce as string, expectedNonce, StringComparison.Ordinal))
        {
            logger.LogWarning("An id token from the provider {Provider} was refused: its nonce does not match this sign-in", provider.Slug);
            throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "The id token does not belong to this sign-in (nonce).");
        }

        if (!claims.TryGetValue("sub", out var subject) || subject is not string { Length: > 0 })
        {
            throw new OidcProviderException(OidcErrorCodes.MissingSubject, "The id token has no subject.");
        }

        if (claims.TryGetValue("aud", out var audience) && audience is IEnumerable<object> audiences && audiences.Count() > 1)
        {
            if (!claims.TryGetValue("azp", out var authorizedParty) || !string.Equals(authorizedParty as string, provider.ClientId, StringComparison.Ordinal))
            {
                throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "The id token has several audiences and was not issued to this client (azp).");
            }
        }

        return new Dictionary<string, object>(claims);
    }

    private static string Describe(Exception? exception) => exception switch
    {
        SecurityTokenExpiredException => "The id token has expired.",
        SecurityTokenNotYetValidException => "The id token is not valid yet.",
        SecurityTokenInvalidAudienceException => "The id token was issued for another client (audience).",
        SecurityTokenInvalidIssuerException => "The id token names another issuer.",
        SecurityTokenSignatureKeyNotFoundException => "The id token was signed with a key the provider does not publish.",
        SecurityTokenInvalidSignatureException => "The id token's signature does not match the provider's keys.",
        SecurityTokenInvalidAlgorithmException => "The id token uses a signing algorithm this server does not accept.",
        SecurityTokenException => "The id token is not valid.",
        _ => "The id token could not be read."
    };
}
