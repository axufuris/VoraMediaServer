using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Vora.Application.Auth.External.Oidc;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth.Oidc;

public class OidcIdTokenValidatorTests
{
    private const string Nonce = "nonce-1";

    private readonly MockOidcProvider _idp = new();
    private readonly AuthProvider _provider = OidcTestKit.Provider();
    private readonly ManualClock _clock = new();
    private readonly Microsoft.Extensions.Caching.Memory.MemoryCache _cache = new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
    private readonly OidcProviderClient _client;
    private readonly OidcIdTokenValidator _validator;

    public OidcIdTokenValidatorTests()
    {
        _client = OidcTestKit.Client(_idp, cache: _cache);
        _validator = new OidcIdTokenValidator(_client, _clock, NullLogger<OidcIdTokenValidator>.Instance);
    }

    private async Task<IReadOnlyDictionary<string, object>> ValidateAsync(string token, string nonce = Nonce)
    {
        var discovery = await _client.GetDiscoveryAsync(_provider, false, TestContext.Current.CancellationToken);
        return await _validator.ValidateAsync(_provider, discovery, token, nonce, TestContext.Current.CancellationToken);
    }

    private async Task<string> RefusedAsync(string token, string nonce = Nonce)
    {
        var failure = await Assert.ThrowsAsync<OidcProviderException>(() => ValidateAsync(token, nonce));
        failure.Code.Should().Be(OidcErrorCodes.IdTokenInvalid);
        return failure.Message;
    }

    [Fact]
    public async Task A_good_token_is_accepted_and_its_claims_are_returned()
    {
        var claims = await ValidateAsync(_idp.IssueIdToken(Nonce));

        claims["sub"].Should().Be("subject-1");
        claims["email"].Should().Be("sam@example.com");
        claims["nonce"].Should().Be(Nonce);
    }

    [Fact]
    public async Task A_token_made_for_another_sign_in_is_refused_by_its_nonce()
    {
        (await RefusedAsync(_idp.IssueIdToken("someone-elses-nonce"))).Should().Contain("nonce");
    }

    [Fact]
    public async Task A_token_without_a_nonce_is_refused()
    {
        (await RefusedAsync(_idp.IssueIdToken(Nonce, payload => payload.Remove("nonce")))).Should().Contain("nonce");
    }

    [Fact]
    public async Task A_token_issued_to_another_client_is_refused()
    {
        (await RefusedAsync(_idp.IssueIdToken(Nonce, payload => payload["aud"] = "another-client"))).Should().Contain("audience");
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        (await RefusedAsync(_idp.IssueIdToken(Nonce, payload => payload["iss"] = "https://evil.example.com"))).Should().Contain("issuer");
    }

    [Fact]
    public async Task An_expired_token_is_refused_and_one_inside_the_clock_skew_is_not()
    {
        var expiredLongAgo = _idp.IssueIdToken(Nonce, expires: DateTime.UtcNow.AddMinutes(-30));
        var justExpired = _idp.IssueIdToken(Nonce, expires: DateTime.UtcNow.AddSeconds(-30));

        (await RefusedAsync(expiredLongAgo)).Should().Contain("expired");
        (await ValidateAsync(justExpired)).Should().ContainKey("sub");
    }

    [Fact]
    public async Task The_check_uses_the_servers_clock()
    {
        var token = _idp.IssueIdToken(Nonce);
        _clock.Now = DateTimeOffset.UtcNow.AddHours(1);

        (await RefusedAsync(token)).Should().Contain("expired");
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var forged = _idp.IssueIdToken(Nonce, signWith: MockOidcProvider.AnotherKey());

        (await RefusedAsync(forged)).Should().Contain("signature");
    }

    [Fact]
    public async Task A_token_that_claims_no_signature_at_all_is_refused()
    {
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode($"{{\"iss\":\"{MockOidcProvider.Issuer}\",\"aud\":\"{MockOidcProvider.ClientId}\",\"sub\":\"subject-1\",\"nonce\":\"{Nonce}\",\"exp\":{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}");

        await RefusedAsync($"{header}.{payload}.");
    }

    [Fact]
    public async Task A_token_signed_with_the_client_secret_as_a_shared_key_is_refused()
    {
        var shared = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(new string('s', 64))) { KeyId = "key-1" };
        var forged = _idp.IssueIdToken(Nonce, signWith: shared, algorithm: SecurityAlgorithms.HmacSha256);

        await RefusedAsync(forged);
    }

    [Fact]
    public async Task A_token_with_several_audiences_needs_this_client_as_the_authorized_party()
    {
        var withoutParty = _idp.IssueIdToken(Nonce, payload => payload["aud"] = new JsonArray(MockOidcProvider.ClientId, "another-api"));
        var wrongParty = _idp.IssueIdToken(Nonce, payload =>
        {
            payload["aud"] = new JsonArray(MockOidcProvider.ClientId, "another-api");
            payload["azp"] = "another-client";
        });
        var rightParty = _idp.IssueIdToken(Nonce, payload =>
        {
            payload["aud"] = new JsonArray(MockOidcProvider.ClientId, "another-api");
            payload["azp"] = MockOidcProvider.ClientId;
        });

        await RefusedAsync(withoutParty);
        await RefusedAsync(wrongParty);
        (await ValidateAsync(rightParty)).Should().ContainKey("sub");
    }

    [Fact]
    public async Task A_token_without_a_subject_is_refused()
    {
        var failure = await Assert.ThrowsAsync<OidcProviderException>(() => ValidateAsync(_idp.IssueIdToken(Nonce, payload => payload.Remove("sub"))));

        failure.Code.Should().Be(OidcErrorCodes.MissingSubject);
    }

    [Fact]
    public async Task A_key_the_provider_has_since_rotated_in_is_picked_up_once()
    {
        await ValidateAsync(_idp.IssueIdToken(Nonce));
        _idp.RotateKey("key-2");
        _cache.Remove("oidc:keys-refreshed:" + _idp.JwksUri);

        var claims = await ValidateAsync(_idp.IssueIdToken(Nonce));

        claims["sub"].Should().Be("subject-1");
        _idp.Count("/jwks").Should().Be(2);
    }

    [Fact]
    public async Task Keys_are_not_fetched_again_and_again_for_tokens_signed_with_unknown_keys()
    {
        await ValidateAsync(_idp.IssueIdToken(Nonce));
        var unknown = _idp.IssueIdToken(Nonce, signWith: MockOidcProvider.AnotherKey("unknown-key"));

        await RefusedAsync(unknown);
        await RefusedAsync(unknown);

        _idp.Count("/jwks").Should().Be(1);
    }

    [Fact]
    public async Task Garbage_instead_of_a_token_is_refused()
    {
        await RefusedAsync("not.a.token");
        await RefusedAsync(string.Empty);
    }
}
