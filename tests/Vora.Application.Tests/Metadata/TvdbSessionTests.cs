using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Tvdb;

namespace Vora.Application.Tests.Metadata;

public class TvdbSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 20, 0, 0, TimeSpan.Zero);

    private static string Jwt(DateTimeOffset expiry, string subject = "vora")
    {
        static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Encode($"{{\"sub\":\"{subject}\",\"exp\":{expiry.ToUnixTimeSeconds()}}}")}.signature";
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemorySettings : IPluginSettingsProvider
    {
        public ConcurrentDictionary<string, string> Values { get; } = new();
        public int Writes;

        public Task<string?> GetSettingAsync(string pluginId, string key) =>
            Task.FromResult(Values.TryGetValue($"{pluginId}:{key}", out var value) ? value : null);

        public Task SetSettingAsync(string pluginId, string key, string value)
        {
            Interlocked.Increment(ref Writes);
            Values[$"{pluginId}:{key}"] = value;
            return Task.CompletedTask;
        }

        public Task<string> GetMetadataLanguageAsync() => Task.FromResult("eng");
    }

    private sealed class FakeTvdb : HttpMessageHandler
    {
        public string IssuedToken { get; set; } = string.Empty;
        public HttpStatusCode LoginStatus { get; set; } = HttpStatusCode.OK;
        public int Logins;
        public List<string?> BearerTokensSeen { get; } = new();
        public List<string> LoginBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Logins);
                if (request.Content != null)
                {
                    var body = await request.Content.ReadAsStringAsync(cancellationToken);
                    lock (LoginBodies) LoginBodies.Add(body);
                }
                await Task.Delay(20, cancellationToken);
                return LoginStatus == HttpStatusCode.OK
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"status\":\"success\",\"data\":{{\"token\":\"{IssuedToken}\"}}}}") }
                    : new HttpResponseMessage(LoginStatus);
            }

            var bearer = request.Headers.Authorization?.Parameter;
            lock (BearerTokensSeen) BearerTokensSeen.Add(bearer);
            return bearer == IssuedToken && IssuedToken.Length > 0
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
    }

    private static (HttpClient Http, IServiceScopeFactory Scopes, InMemorySettings Settings, FakeTvdb Tvdb) Harness(string? storedToken, string? apiKey = "key-123", string? subscriberPin = null)
    {
        var settings = new InMemorySettings();
        if (storedToken != null) settings.Values[$"{TvdbSession.PluginId}:{TvdbSession.TokenKey}"] = storedToken;
        if (apiKey != null) settings.Values[$"{TvdbSession.PluginId}:{TvdbSession.ApiKeyKey}"] = apiKey;
        if (subscriberPin != null) settings.Values[$"{TvdbSession.PluginId}:{TvdbSession.SubscriberPinKey}"] = subscriberPin;

        var services = new ServiceCollection().AddSingleton<IPluginSettingsProvider>(settings).BuildServiceProvider();
        var tvdb = new FakeTvdb();
        var http = new HttpClient(tvdb) { BaseAddress = new Uri("https://api4.thetvdb.com/v4/") };
        return (http, services.GetRequiredService<IServiceScopeFactory>(), settings, tvdb);
    }

    // TVDB v4 issues keys per project, not per person. A licensed key logs in on
    // its own; a user-supported (free) one also needs the PIN of the subscriber
    // whose account pays for the lookups. One endpoint serves both, and the
    // difference is whether the body carries a pin at all — a blank one is
    // rejected, so an empty setting must not reach the wire.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_login_without_a_subscriber_pin_does_not_send_the_field(string? pin)
    {
        var body = TvdbSession.BuildLoginBody("key-123", pin);

        body.Should().Be("{\"apikey\":\"key-123\"}");
        body.Should().NotContain("pin");
    }

    [Fact]
    public void A_login_with_a_subscriber_pin_sends_both()
    {
        var body = TvdbSession.BuildLoginBody("key-123", "ABCD1234");

        body.Should().Be("{\"apikey\":\"key-123\",\"pin\":\"ABCD1234\"}");
    }

    // A PIN pasted out of the TVDB dashboard routinely arrives with whitespace.
    [Fact]
    public void Surrounding_whitespace_is_trimmed_from_both_values()
    {
        TvdbSession.BuildLoginBody("  key-123 ", "  ABCD1234  ")
            .Should().Be("{\"apikey\":\"key-123\",\"pin\":\"ABCD1234\"}");
    }

    [Fact]
    public async Task A_configured_subscriber_pin_reaches_the_login_request()
    {
        var (http, scopes, _, tvdb) = Harness(storedToken: null, subscriberPin: "ABCD1234");
        tvdb.IssuedToken = Jwt(Now.AddDays(30));

        await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        tvdb.LoginBodies.Should().ContainSingle().Which.Should().Contain("\"pin\":\"ABCD1234\"");
    }

    [Fact]
    public async Task A_licensed_key_still_logs_in_with_no_pin_configured()
    {
        var (http, scopes, _, tvdb) = Harness(storedToken: null);
        tvdb.IssuedToken = Jwt(Now.AddDays(30));

        var token = await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().Be(tvdb.IssuedToken);
        tvdb.LoginBodies.Should().ContainSingle().Which.Should().NotContain("pin");
    }

    [Fact]
    public void The_expiry_is_read_from_the_token()
    {
        var expiry = new DateTimeOffset(2026, 9, 10, 14, 17, 9, TimeSpan.Zero);

        TvdbSession.ReadExpiry(Jwt(expiry)).Should().Be(expiry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.!!!.c")]
    [InlineData("a.e30.c")]
    public void A_token_with_no_readable_expiry_has_none(string? token)
    {
        TvdbSession.ReadExpiry(token).Should().BeNull();
    }

    // The production failure: the stored token expired on 2026-09-10 and was
    // kept anyway, because renewal only happened when no token was stored.
    [Fact]
    public void An_expired_token_needs_renewal()
    {
        TvdbSession.NeedsRenewal(Jwt(new DateTimeOffset(2026, 9, 10, 14, 17, 9, TimeSpan.Zero)), Now).Should().BeTrue();
    }

    [Fact]
    public void A_token_about_to_expire_is_renewed_ahead_of_time()
    {
        TvdbSession.NeedsRenewal(Jwt(Now.AddHours(6)), Now).Should().BeTrue();
    }

    [Fact]
    public void A_token_with_weeks_left_is_kept()
    {
        TvdbSession.NeedsRenewal(Jwt(Now.AddDays(20)), Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_token_needs_one(string? token)
    {
        TvdbSession.NeedsRenewal(token, Now).Should().BeTrue();
    }

    // A token whose expiry can't be read is used as-is; if TVDB rejects it the
    // 401 path renews it. Discarding it up front would log in on every call.
    [Fact]
    public void An_opaque_token_is_kept_until_rejected()
    {
        TvdbSession.NeedsRenewal("opaque-token", Now).Should().BeFalse();
    }

    [Fact]
    public async Task An_expired_stored_token_is_replaced_by_logging_in()
    {
        var expired = Jwt(Now.AddDays(-4));
        var fresh = Jwt(Now.AddDays(30), "fresh");
        var (http, scopes, settings, tvdb) = Harness(expired);
        tvdb.IssuedToken = fresh;

        var token = await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().Be(fresh);
        tvdb.Logins.Should().Be(1);
        settings.Values[$"{TvdbSession.PluginId}:{TvdbSession.TokenKey}"].Should().Be(fresh);
    }

    [Fact]
    public async Task A_valid_stored_token_is_used_without_logging_in()
    {
        var valid = Jwt(Now.AddDays(20));
        var (http, scopes, settings, tvdb) = Harness(valid);

        var token = await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().Be(valid);
        tvdb.Logins.Should().Be(0);
        settings.Writes.Should().Be(0);
    }

    // Returning the stale token after a failed login would put every caller back
    // into the silent-401 state this fixes.
    [Fact]
    public async Task A_failed_login_returns_no_token_rather_than_the_expired_one()
    {
        var (http, scopes, settings, tvdb) = Harness(Jwt(Now.AddDays(-4)));
        tvdb.LoginStatus = HttpStatusCode.Unauthorized;

        var token = await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().BeNull();
        settings.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Without_an_api_key_there_is_no_token_and_no_login()
    {
        var (http, scopes, _, tvdb) = Harness(Jwt(Now.AddDays(-4)), apiKey: null);

        var token = await TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().BeNull();
        tvdb.Logins.Should().Be(0);
    }

    [Fact]
    public async Task A_rejected_token_is_renewed_even_before_its_expiry()
    {
        var revoked = Jwt(Now.AddDays(20), "revoked");
        var fresh = Jwt(Now.AddDays(30), "fresh");
        var (http, scopes, _, tvdb) = Harness(revoked);
        tvdb.IssuedToken = fresh;

        var token = await TvdbSession.GetTokenAsync(http, scopes, revoked, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().Be(fresh);
        tvdb.Logins.Should().Be(1);
    }

    // Parallel scan units all get a 401 with the same old token. Once one of
    // them has renewed it, the rest must pick up the new token, not log in again.
    [Fact]
    public async Task A_token_someone_else_already_renewed_is_not_renewed_twice()
    {
        var old = Jwt(Now.AddDays(20), "old");
        var renewed = Jwt(Now.AddDays(30), "renewed");
        var (http, scopes, _, tvdb) = Harness(renewed);

        var token = await TvdbSession.GetTokenAsync(http, scopes, old, new FixedTime(Now), TestContext.Current.CancellationToken);

        token.Should().Be(renewed);
        tvdb.Logins.Should().Be(0);
    }

    [Fact]
    public async Task Many_callers_hitting_an_expired_token_log_in_once()
    {
        var fresh = Jwt(Now.AddDays(30), "fresh");
        var (http, scopes, _, tvdb) = Harness(Jwt(Now.AddDays(-4)));
        tvdb.IssuedToken = fresh;

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => TvdbSession.GetTokenAsync(http, scopes, null, new FixedTime(Now), TestContext.Current.CancellationToken)));

        tokens.Should().AllBe(fresh);
        tvdb.Logins.Should().Be(1);
    }

    [Fact]
    public async Task A_request_rejected_with_401_is_retried_once_with_a_renewed_token()
    {
        var revoked = Jwt(DateTimeOffset.UtcNow.AddDays(20), "revoked");
        var fresh = Jwt(DateTimeOffset.UtcNow.AddDays(30), "fresh");
        var (http, scopes, _, tvdb) = Harness(revoked);
        tvdb.IssuedToken = fresh;

        using var response = await TvdbSession.SendAsync(http, scopes, "search?query=Dead%20City&type=series", revoked, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tvdb.BearerTokensSeen.Should().Equal(revoked, fresh);
        tvdb.Logins.Should().Be(1);
    }

    [Fact]
    public async Task A_request_with_a_good_token_is_sent_once()
    {
        var valid = Jwt(DateTimeOffset.UtcNow.AddDays(20));
        var (http, scopes, _, tvdb) = Harness(valid);
        tvdb.IssuedToken = valid;

        using var response = await TvdbSession.SendAsync(http, scopes, "series/417549/extended", valid, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tvdb.BearerTokensSeen.Should().ContainSingle();
        tvdb.Logins.Should().Be(0);
    }

    [Fact]
    public async Task When_renewal_fails_the_request_reports_unauthorized_instead_of_looping()
    {
        var revoked = Jwt(DateTimeOffset.UtcNow.AddDays(20), "revoked");
        var (http, scopes, _, tvdb) = Harness(revoked);
        tvdb.LoginStatus = HttpStatusCode.Unauthorized;

        using var response = await TvdbSession.SendAsync(http, scopes, "series/417549/extended", revoked, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        tvdb.BearerTokensSeen.Should().ContainSingle();
    }
}
