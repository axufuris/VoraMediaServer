using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Auth.External.Oidc;

public interface IOidcProviderClient
{
    Task<OidcDiscovery> GetDiscoveryAsync(AuthProvider provider, bool refresh, CancellationToken cancellationToken);
    Task<OidcTokenResponse> ExchangeCodeAsync(AuthProvider provider, OidcDiscovery discovery, string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(OidcDiscovery discovery, bool refresh, CancellationToken cancellationToken);
    Task<JsonElement?> GetUserInfoAsync(OidcDiscovery discovery, string accessToken, CancellationToken cancellationToken);
}

public sealed class OidcProviderClient(
    IHttpClientFactory httpClients,
    IMemoryCache cache,
    IAuthSecretProtector secretProtector,
    ILogger<OidcProviderClient> logger) : IOidcProviderClient
{
    public const string HttpClientName = "OidcProvider";

    private static readonly TimeSpan DiscoveryLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan KeyRefreshInterval = TimeSpan.FromSeconds(30);

    public async Task<OidcDiscovery> GetDiscoveryAsync(AuthProvider provider, bool refresh, CancellationToken cancellationToken)
    {
        var issuer = provider.Issuer ?? throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, "The provider has no issuer.");
        var cacheKey = "oidc:discovery:" + issuer;
        if (!refresh && cache.TryGetValue(cacheKey, out OidcDiscovery? cached) && cached is not null)
        {
            return cached;
        }

        var url = issuer.TrimEnd('/') + "/.well-known/openid-configuration";
        var body = await GetBodyAsync(new HttpRequestMessage(HttpMethod.Get, url), OidcErrorCodes.DiscoveryFailed, "its discovery document", cancellationToken);
        var discovery = ParseDiscovery(issuer, body);

        cache.Set(cacheKey, discovery, DiscoveryLifetime);
        if (issuer.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("The provider {Provider} is reached over plain http, so its tokens and the client secret cross the network unprotected", provider.Slug);
        }

        return discovery;
    }

    public async Task<OidcTokenResponse> ExchangeCodeAsync(AuthProvider provider, OidcDiscovery discovery, string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
    {
        var clientId = provider.ClientId ?? throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, "The provider has no client id.");
        var secret = ReadSecret(provider);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint);
        if (secret is not null && !discovery.PrefersPostAuthentication)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(secret)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            form["client_id"] = clientId;
            if (secret is not null)
            {
                form["client_secret"] = secret;
            }
        }

        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = await GetBodyAsync(request, OidcErrorCodes.TokenExchangeFailed, "the token exchange", cancellationToken, acceptClientErrors: true);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, "The provider's token answer was not a JSON object.");
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, $"The provider refused the code: {error.GetString()}.");
            }

            var idToken = ReadString(root, "id_token");
            if (idToken is null)
            {
                throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, "The provider's token answer had no id_token. Check that the openid scope is allowed for this client.");
            }

            return new OidcTokenResponse(idToken, ReadString(root, "access_token"));
        }
        catch (JsonException)
        {
            throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, "The provider's token answer was not valid JSON.");
        }
    }

    public async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(OidcDiscovery discovery, bool refresh, CancellationToken cancellationToken)
    {
        var cacheKey = "oidc:keys:" + discovery.JwksUri;
        var throttleKey = "oidc:keys-refreshed:" + discovery.JwksUri;
        if (refresh && cache.TryGetValue(throttleKey, out _) && cache.TryGetValue(cacheKey, out IReadOnlyList<SecurityKey>? throttled) && throttled is not null)
        {
            return throttled;
        }

        if (!refresh && cache.TryGetValue(cacheKey, out IReadOnlyList<SecurityKey>? cached) && cached is not null)
        {
            return cached;
        }

        var body = await GetBodyAsync(new HttpRequestMessage(HttpMethod.Get, discovery.JwksUri), OidcErrorCodes.IdTokenInvalid, "its signing keys", cancellationToken);
        IReadOnlyList<SecurityKey> keys;
        try
        {
            keys = new JsonWebKeySet(body).Keys
                .Where(key => string.IsNullOrEmpty(key.Use) || string.Equals(key.Use, "sig", StringComparison.Ordinal))
                .Cast<SecurityKey>()
                .ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "The provider's signing keys could not be read.");
        }

        if (keys.Count == 0)
        {
            throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "The provider published no signing keys.");
        }

        cache.Set(cacheKey, keys, DiscoveryLifetime);
        cache.Set(throttleKey, true, KeyRefreshInterval);
        return keys;
    }

    public async Task<JsonElement?> GetUserInfoAsync(OidcDiscovery discovery, string accessToken, CancellationToken cancellationToken)
    {
        if (discovery.UserInfoEndpoint is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, discovery.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var body = await GetBodyAsync(request, OidcErrorCodes.IdTokenInvalid, "its user info", cancellationToken);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception ex) when (ex is OidcProviderException or JsonException)
        {
            logger.LogWarning("The provider's user info could not be read, so the sign-in goes on with the id token alone");
            return null;
        }
    }

    private static OidcDiscovery ParseDiscovery(string issuer, string body)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, "The discovery document is not valid JSON. Check the issuer address.");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, "The discovery document is not a JSON object.");
        }

        var named = ReadString(root, "issuer");
        if (!string.Equals(named, issuer, StringComparison.Ordinal))
        {
            throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, $"The discovery document names the issuer \"{named}\", not the one entered. Copy the issuer exactly as the provider shows it.");
        }

        var secure = issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        string Endpoint(string property)
        {
            var value = ReadString(root, property) ?? throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, $"The discovery document has no {property}.");
            return CheckEndpoint(value, property, secure);
        }

        var userInfo = ReadString(root, "userinfo_endpoint");
        var methods = ReadStrings(root, "code_challenge_methods_supported");
        var authMethods = ReadStrings(root, "token_endpoint_auth_methods_supported");

        return new OidcDiscovery(
            issuer,
            Endpoint("authorization_endpoint"),
            Endpoint("token_endpoint"),
            Endpoint("jwks_uri"),
            userInfo is null ? null : CheckEndpoint(userInfo, "userinfo_endpoint", secure),
            methods is null || methods.Contains("S256", StringComparer.Ordinal),
            methods is not null && methods.Contains("S256", StringComparer.Ordinal),
            authMethods is not null && authMethods.Contains("client_secret_post", StringComparer.Ordinal) && !authMethods.Contains("client_secret_basic", StringComparer.Ordinal));
    }

    private static string CheckEndpoint(string value, string property, bool secure)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, $"The discovery document's {property} is not a web address.");
        }

        if (secure && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, $"The discovery document's {property} uses plain http though the issuer uses https, which would send credentials unprotected.");
        }

        return value;
    }

    private string? ReadSecret(AuthProvider provider)
    {
        if (string.IsNullOrEmpty(provider.ClientSecretCiphertext))
        {
            return null;
        }

        try
        {
            return secretProtector.Unprotect(provider.ClientSecretCiphertext);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            logger.LogError("The saved client secret of the provider {Provider} cannot be decrypted, so its keys have changed; enter the secret again", provider.Slug);
            throw new OidcProviderException(OidcErrorCodes.TokenExchangeFailed, "The saved client secret cannot be read. Enter it again on the provider's page.");
        }
    }

    private async Task<string> GetBodyAsync(HttpRequestMessage request, string errorCode, string what, CancellationToken cancellationToken, bool acceptClientErrors = false)
    {
        using var disposable = request;
        var client = httpClients.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OidcProviderException(errorCode, $"The provider did not answer for {what} in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new OidcProviderException(errorCode, $"The provider could not be reached for {what}: {ex.HttpRequestError}.");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                throw new OidcProviderException(errorCode, $"The provider answered {what} with a redirect, which is not followed. Use the address the provider itself gives.");
            }

            var acceptable = response.IsSuccessStatusCode || (acceptClientErrors && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
            if (!acceptable)
            {
                throw new OidcProviderException(errorCode, $"The provider answered {what} with status {status}.");
            }

            try
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new OidcProviderException(errorCode, $"The provider's answer for {what} was too large.");
            }
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static List<string>? ReadStrings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).ToList()
            : null;
}
