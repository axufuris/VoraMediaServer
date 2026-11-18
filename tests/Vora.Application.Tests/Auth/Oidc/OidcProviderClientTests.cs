using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Oidc;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth.Oidc;

public class OidcProviderClientTests
{
    private readonly MockOidcProvider _idp = new();
    private readonly AuthProvider _provider = OidcTestKit.Provider();
    private readonly OidcProviderClient _client;

    public OidcProviderClientTests()
    {
        _client = OidcTestKit.Client(_idp);
    }

    private Task<OidcDiscovery> DiscoverAsync(bool refresh = false) => _client.GetDiscoveryAsync(_provider, refresh, TestContext.Current.CancellationToken);

    private static async Task<OidcProviderException> FailureAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<OidcProviderException>(action));

    [Fact]
    public async Task The_discovery_document_is_read_and_kept_for_later_sign_ins()
    {
        var first = await DiscoverAsync();
        var again = await DiscoverAsync();

        first.AuthorizationEndpoint.Should().Be("https://idp.example.com/realms/vora/auth");
        first.TokenEndpoint.Should().EndWith("/token");
        first.JwksUri.Should().EndWith("/jwks");
        first.SupportsS256.Should().BeTrue();
        again.Should().BeSameAs(first);
        _idp.Count("/.well-known/openid-configuration").Should().Be(1);
        (await DiscoverAsync(refresh: true)).Should().NotBeSameAs(first);
        _idp.Count("/.well-known/openid-configuration").Should().Be(2);
    }

    [Fact]
    public async Task The_discovery_address_is_the_issuer_with_the_well_known_path()
    {
        _provider.Issuer = MockOidcProvider.Issuer + "/";
        _idp.DiscoveryIssuer = MockOidcProvider.Issuer + "/";

        await DiscoverAsync();

        _idp.Requests.Single().Uri.AbsoluteUri.Should().Be("https://idp.example.com/realms/vora/.well-known/openid-configuration");
    }

    [Fact]
    public async Task A_discovery_document_for_another_issuer_is_refused()
    {
        _idp.DiscoveryIssuer = "https://evil.example.com";

        var failure = await FailureAsync(() => DiscoverAsync());

        failure.Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
        failure.Message.Should().Contain("not the one entered");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"issuer\":\"https://idp.example.com/realms/vora\"}")]
    public async Task A_discovery_document_that_is_not_usable_is_refused(string body)
    {
        _idp.DiscoveryBodyOverride = body;

        (await FailureAsync(() => DiscoverAsync())).Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
    }

    [Fact]
    public async Task A_redirect_from_the_provider_is_reported_and_not_followed()
    {
        _idp.DiscoveryStatus = HttpStatusCode.Found;

        var failure = await FailureAsync(() => DiscoverAsync());

        failure.Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
        failure.Message.Should().Contain("redirect");
        _idp.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_error_answer_from_the_provider_is_refused(HttpStatusCode status)
    {
        _idp.DiscoveryStatus = status;

        (await FailureAsync(() => DiscoverAsync())).Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("auth")]
    [InlineData("jwks")]
    public async Task An_endpoint_that_drops_from_https_to_http_is_refused(string which)
    {
        _idp.TokenEndpoint = which == "token" ? "http://idp.example.com/token" : _idp.TokenEndpoint;
        _idp.AuthorizationEndpoint = which == "auth" ? "http://idp.example.com/auth" : _idp.AuthorizationEndpoint;
        _idp.JwksUri = which == "jwks" ? "http://idp.example.com/jwks" : _idp.JwksUri;

        var failure = await FailureAsync(() => DiscoverAsync());

        failure.Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
        failure.Message.Should().Contain("plain http");
    }

    [Theory]
    [InlineData("ftp://idp.example.com/token")]
    [InlineData("/token")]
    [InlineData("javascript:alert(1)")]
    public async Task An_endpoint_that_is_not_a_web_address_is_refused(string endpoint)
    {
        _idp.TokenEndpoint = endpoint;

        (await FailureAsync(() => DiscoverAsync())).Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
    }

    [Fact]
    public async Task A_private_network_provider_over_plain_http_is_allowed_with_its_own_endpoints()
    {
        _provider.Issuer = "http://192.168.0.20:9000/application/o/vora/";
        _idp.DiscoveryIssuer = _provider.Issuer;
        _idp.TokenEndpoint = "http://192.168.0.20:9000/application/o/token/";
        _idp.AuthorizationEndpoint = "http://192.168.0.20:9000/application/o/authorize/";
        _idp.JwksUri = "http://192.168.0.20:9000/application/o/vora/jwks/";

        var discovery = await DiscoverAsync();

        discovery.TokenEndpoint.Should().StartWith("http://192.168.0.20:9000");
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("S256", true, true)]
    [InlineData("plain", false, false)]
    public async Task Whether_PKCE_S256_is_offered_comes_from_the_discovery_document(string? method, bool supports, bool advertises)
    {
        _idp.CodeChallengeMethods = method is null ? null : [method];

        var discovery = await DiscoverAsync();

        discovery.SupportsS256.Should().Be(supports);
        discovery.AdvertisesS256.Should().Be(advertises);
    }

    [Fact]
    public async Task The_code_is_traded_with_the_PKCE_verifier_and_the_secret_in_basic_authentication()
    {
        _idp.NextIdToken = "the-id-token";
        var discovery = await DiscoverAsync();

        var tokens = await _client.ExchangeCodeAsync(_provider, discovery, "the-code", "https://vora.example.com/api/auth/oidc/authentik/callback", "the-verifier", TestContext.Current.CancellationToken);

        tokens.IdToken.Should().Be("the-id-token");
        tokens.AccessToken.Should().Be("access-token-1");
        var form = _idp.LastTokenForm();
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("the-code");
        form["redirect_uri"].Should().Be("https://vora.example.com/api/auth/oidc/authentik/callback");
        form["code_verifier"].Should().Be("the-verifier");
        form.Should().NotContainKey("client_secret");
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{MockOidcProvider.ClientId}:{OidcTestKit.Secret}"));
        _idp.LastTokenRequest!.Authorization.Should().Be("Basic " + expected);
    }

    [Fact]
    public async Task A_provider_that_only_takes_the_secret_in_the_body_gets_it_there()
    {
        _idp.AuthMethods = ["client_secret_post"];
        var discovery = await DiscoverAsync();

        await _client.ExchangeCodeAsync(_provider, discovery, "code", "https://vora.example.com/cb", "verifier", TestContext.Current.CancellationToken);

        var form = _idp.LastTokenForm();
        form["client_id"].Should().Be(MockOidcProvider.ClientId);
        form["client_secret"].Should().Be(OidcTestKit.Secret);
        _idp.LastTokenRequest!.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task A_public_client_sends_only_its_id_and_the_verifier()
    {
        _provider.ClientSecretCiphertext = null;
        var discovery = await DiscoverAsync();

        await _client.ExchangeCodeAsync(_provider, discovery, "code", "https://vora.example.com/cb", "verifier", TestContext.Current.CancellationToken);

        var form = _idp.LastTokenForm();
        form["client_id"].Should().Be(MockOidcProvider.ClientId);
        form.Should().NotContainKey("client_secret");
        _idp.LastTokenRequest!.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task A_refused_code_and_an_answer_without_an_id_token_are_reported_without_the_secret()
    {
        var discovery = await DiscoverAsync();
        _idp.TokenError = "invalid_grant";
        var refused = await FailureAsync(() => _client.ExchangeCodeAsync(_provider, discovery, "code", "https://vora.example.com/cb", "verifier", TestContext.Current.CancellationToken));

        _idp.TokenError = null;
        _idp.OmitIdToken = true;
        var noToken = await FailureAsync(() => _client.ExchangeCodeAsync(_provider, discovery, "code", "https://vora.example.com/cb", "verifier", TestContext.Current.CancellationToken));

        refused.Code.Should().Be(OidcErrorCodes.TokenExchangeFailed);
        refused.Message.Should().Contain("invalid_grant").And.NotContain(OidcTestKit.Secret);
        noToken.Message.Should().Contain("id_token");
    }

    [Fact]
    public async Task A_saved_secret_that_cannot_be_decrypted_asks_the_admin_to_enter_it_again()
    {
        var protector = Substitute.For<Vora.Application.Auth.External.IAuthSecretProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("keys changed"));
        var client = OidcTestKit.Client(_idp, protector);
        var discovery = await client.GetDiscoveryAsync(_provider, false, TestContext.Current.CancellationToken);

        var failure = await FailureAsync(() => client.ExchangeCodeAsync(_provider, discovery, "code", "https://vora.example.com/cb", "verifier", TestContext.Current.CancellationToken));

        failure.Message.Should().Contain("Enter it again");
        _idp.LastTokenRequest.Should().BeNull();
    }

    [Fact]
    public async Task The_signing_keys_are_read_kept_and_read_again_when_asked()
    {
        var discovery = await DiscoverAsync();

        var first = await _client.GetSigningKeysAsync(discovery, false, TestContext.Current.CancellationToken);
        var again = await _client.GetSigningKeysAsync(discovery, false, TestContext.Current.CancellationToken);

        first.Should().ContainSingle().Which.KeyId.Should().Be("key-1");
        again.Should().BeSameAs(first);
        _idp.Count("/jwks").Should().Be(1);
    }

    [Fact]
    public async Task Asking_for_the_keys_again_and_again_does_not_hammer_the_provider()
    {
        var discovery = await DiscoverAsync();
        await _client.GetSigningKeysAsync(discovery, false, TestContext.Current.CancellationToken);

        await _client.GetSigningKeysAsync(discovery, true, TestContext.Current.CancellationToken);
        await _client.GetSigningKeysAsync(discovery, true, TestContext.Current.CancellationToken);

        _idp.Count("/jwks").Should().Be(1);
    }

    [Fact]
    public async Task User_info_is_read_with_the_access_token_and_only_when_the_provider_has_it()
    {
        _idp.UserInfo = new System.Text.Json.Nodes.JsonObject { ["sub"] = "subject-1", ["email"] = "sam@example.com" };
        var discovery = await DiscoverAsync();

        var info = await _client.GetUserInfoAsync(discovery, "access-token-1", TestContext.Current.CancellationToken);

        info!.Value.GetProperty("email").GetString().Should().Be("sam@example.com");
        _idp.Requests.Last().Authorization.Should().Be("Bearer access-token-1");
        (await _client.GetUserInfoAsync(discovery with { UserInfoEndpoint = null }, "access-token-1", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_user_info_failure_does_not_stop_the_sign_in()
    {
        _idp.UserInfo = null;
        var discovery = await DiscoverAsync();

        (await _client.GetUserInfoAsync(discovery, "access-token-1", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task An_answer_larger_than_the_cap_is_refused()
    {
        var big = new string('x', 5000);
        _idp.DiscoveryBodyOverride = "{\"issuer\":\"" + MockOidcProvider.Issuer + "\",\"pad\":\"" + big + "\"}";
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(OidcProviderClient.HttpClientName).Returns(_ => new HttpClient(_idp, disposeHandler: false) { MaxResponseContentBufferSize = 1000 });
        var client = new OidcProviderClient(factory, new MemoryCache(new MemoryCacheOptions()), OidcTestKit.Protector(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OidcProviderClient>.Instance);

        var failure = await FailureAsync(() => client.GetDiscoveryAsync(_provider, false, TestContext.Current.CancellationToken));

        failure.Code.Should().Be(OidcErrorCodes.DiscoveryFailed);
    }
}
