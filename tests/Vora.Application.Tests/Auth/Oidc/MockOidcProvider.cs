using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Vora.Application.Tests.Auth.Oidc;

public sealed class MockOidcProvider : HttpMessageHandler
{
    public const string Issuer = "https://idp.example.com/realms/vora";
    public const string ClientId = "vora-client";

    private RSA _rsa = RSA.Create(2048);

    public string KeyId { get; private set; } = "key-1";
    public string DiscoveryIssuer { get; set; } = Issuer;
    public string TokenEndpoint { get; set; } = "https://idp.example.com/realms/vora/token";
    public string AuthorizationEndpoint { get; set; } = "https://idp.example.com/realms/vora/auth";
    public string JwksUri { get; set; } = "https://idp.example.com/realms/vora/jwks";
    public string? UserInfoEndpoint { get; set; } = "https://idp.example.com/realms/vora/userinfo";
    public IReadOnlyList<string>? CodeChallengeMethods { get; set; } = ["S256"];
    public IReadOnlyList<string>? AuthMethods { get; set; }
    public HttpStatusCode DiscoveryStatus { get; set; } = HttpStatusCode.OK;
    public string? DiscoveryBodyOverride { get; set; }
    public string? TokenError { get; set; }
    public bool OmitIdToken { get; set; }
    public JsonObject? UserInfo { get; set; }
    public string NextIdToken { get; set; } = "id-token";
    public string NextAccessToken { get; set; } = "access-token-1";

    public List<RecordedRequest> Requests { get; } = [];

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? Authorization);

    public RecordedRequest? LastTokenRequest => Requests.LastOrDefault(r => r.Uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal));

    public int Count(string pathEnd) => Requests.Count(r => r.Uri.AbsolutePath.EndsWith(pathEnd, StringComparison.Ordinal));

    public Dictionary<string, string> LastTokenForm()
    {
        var body = LastTokenRequest?.Body ?? string.Empty;
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0].Replace('+', ' ')), parts => Uri.UnescapeDataString(parts.Length > 1 ? parts[1].Replace('+', ' ') : string.Empty));
    }

    public void RotateKey(string keyId)
    {
        _rsa.Dispose();
        _rsa = RSA.Create(2048);
        KeyId = keyId;
    }

    public string IssueIdToken(string nonce, Action<JsonObject>? change = null, SecurityKey? signWith = null, string? algorithm = null, DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var payload = new JsonObject
        {
            ["iss"] = Issuer,
            ["aud"] = ClientId,
            ["sub"] = "subject-1",
            ["nonce"] = nonce,
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expires ?? now.AddMinutes(10)).ToUnixTimeSeconds(),
            ["email"] = "sam@example.com",
            ["email_verified"] = true,
            ["name"] = "Sam"
        };
        change?.Invoke(payload);

        var key = signWith ?? new RsaSecurityKey(_rsa) { KeyId = KeyId };
        var credentials = new SigningCredentials(key, algorithm ?? SecurityAlgorithms.RsaSha256);
        return new JsonWebTokenHandler().CreateToken(payload.ToJsonString(), credentials);
    }

    public static SecurityKey AnotherKey(string keyId = "key-1") => new RsaSecurityKey(RSA.Create(2048)) { KeyId = keyId };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri ?? throw new InvalidOperationException("No address.");
        Requests.Add(new RecordedRequest(request.Method, uri, body, request.Headers.Authorization?.ToString()));

        var path = uri.AbsolutePath;
        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            if (DiscoveryStatus != HttpStatusCode.OK)
            {
                var redirect = new HttpResponseMessage(DiscoveryStatus);
                if ((int)DiscoveryStatus is >= 300 and < 400)
                {
                    redirect.Headers.Location = new Uri("https://elsewhere.example.com/");
                }
                return redirect;
            }

            return Json(DiscoveryBodyOverride ?? Discovery().ToJsonString());
        }

        if (path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            return Json(Jwks().ToJsonString());
        }

        if (path.EndsWith("/token", StringComparison.Ordinal))
        {
            if (TokenError is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"error\":\"{TokenError}\"}}", Encoding.UTF8, "application/json") };
            }

            var answer = new JsonObject { ["access_token"] = NextAccessToken, ["token_type"] = "Bearer" };
            if (!OmitIdToken)
            {
                answer["id_token"] = NextIdToken;
            }
            return Json(answer.ToJsonString());
        }

        if (path.EndsWith("/userinfo", StringComparison.Ordinal))
        {
            return UserInfo is null ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json(UserInfo.ToJsonString());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private JsonObject Discovery()
    {
        var document = new JsonObject
        {
            ["issuer"] = DiscoveryIssuer,
            ["authorization_endpoint"] = AuthorizationEndpoint,
            ["token_endpoint"] = TokenEndpoint,
            ["jwks_uri"] = JwksUri
        };
        if (UserInfoEndpoint is not null) document["userinfo_endpoint"] = UserInfoEndpoint;
        if (CodeChallengeMethods is not null) document["code_challenge_methods_supported"] = new JsonArray(CodeChallengeMethods.Select(m => (JsonNode?)m).ToArray());
        if (AuthMethods is not null) document["token_endpoint_auth_methods_supported"] = new JsonArray(AuthMethods.Select(m => (JsonNode?)m).ToArray());
        return document;
    }

    private JsonObject Jwks()
    {
        var parameters = _rsa.ExportParameters(false);
        return new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA",
                ["kid"] = KeyId,
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
            })
        };
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
