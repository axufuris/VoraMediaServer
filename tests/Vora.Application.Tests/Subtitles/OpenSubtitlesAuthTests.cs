using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.OpenSubtitles;

namespace Vora.Application.Tests.Subtitles;

// OpenSubtitles can be used two ways from one API key: anonymously, or signed in
// so downloads bill to the admin's own (larger) quota. Both modes are the same
// source, so the difference lives entirely in which headers go out and which
// host answers — which is what these pin.
public class OpenSubtitlesAuthTests : IDisposable
{
    private const string ApiKey = "test-api-key";
    private const string VipBaseUrl = "https://vip-api.opensubtitles.com/api/v1/";

    private readonly ScriptedHandler _handler = new();

    public OpenSubtitlesAuthTests() => OpenSubtitlesSubtitleProvider.ResetTransientState();

    public void Dispose()
    {
        OpenSubtitlesSubtitleProvider.ResetTransientState();
        GC.SuppressFinalize(this);
    }

    private OpenSubtitlesSubtitleProvider NewProvider(Dictionary<string, string?> settings)
    {
        var pluginSettings = Substitute.For<IPluginSettingsProvider>();
        pluginSettings.GetSettingAsync(OpenSubtitlesSubtitleProvider.PluginId, Arg.Any<string>())
            .Returns(call => settings.GetValueOrDefault(call.ArgAt<string>(1)));

        var services = new ServiceCollection();
        services.AddSingleton(pluginSettings);
        var provider = services.BuildServiceProvider();

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, disposeHandler: false));

        return new OpenSubtitlesSubtitleProvider(
            factory,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OpenSubtitlesSubtitleProvider>.Instance);
    }

    private static Dictionary<string, string?> ApiKeyOnly() => new()
    {
        ["api_key"] = ApiKey,
        ["auth_mode"] = "API key only",
    };

    private static Dictionary<string, string?> Account(string? username = "someone", string? password = "secret") => new()
    {
        ["api_key"] = ApiKey,
        ["auth_mode"] = "Account (username/password)",
        ["username"] = username,
        ["password"] = password,
    };

    private static SubtitleSearchQuery Query() => new() { ImdbId = "tt1368337", Languages = ["en"] };

    private const string EmptySearchBody = """{"data":[]}""";

    // --- Mode resolution -----------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("API key only")]
    [InlineData("something unrecognised")]
    public void Anything_but_account_resolves_to_api_key_only(string? configured)
    {
        var plan = OpenSubtitlesAuthPlan.Resolve(configured, "someone", "secret");

        plan.Mode.Should().Be(OpenSubtitlesAuthMode.ApiKeyOnly);
        plan.Warning.Should().BeNull();
    }

    [Fact]
    public void Account_mode_with_credentials_resolves_to_account()
    {
        var plan = OpenSubtitlesAuthPlan.Resolve("Account (username/password)", "someone", "secret");

        plan.Mode.Should().Be(OpenSubtitlesAuthMode.Account);
        plan.Username.Should().Be("someone");
    }

    // Failing hard here would take the whole feature down over a half-finished
    // settings page; the anonymous quota still works, so it degrades instead.
    [Theory]
    [InlineData(null, "secret")]
    [InlineData("someone", null)]
    [InlineData("", "secret")]
    [InlineData("   ", "   ")]
    public void Account_mode_without_credentials_falls_back_and_warns(string? username, string? password)
    {
        var plan = OpenSubtitlesAuthPlan.Resolve("Account (username/password)", username, password);

        plan.Mode.Should().Be(OpenSubtitlesAuthMode.ApiKeyOnly);
        plan.Warning.Should().Be(OpenSubtitlesAuthPlan.MissingCredentialsWarning);
    }

    // --- API-key-only mode ---------------------------------------------------

    [Fact]
    public async Task Api_key_mode_never_signs_in()
    {
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(ApiKeyOnly()).SearchAsync(Query());

        _handler.Requests.Should().NotContain(r => r.Path.Contains("login"));
    }

    [Fact]
    public async Task Api_key_mode_sends_the_key_and_no_bearer()
    {
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(ApiKeyOnly()).SearchAsync(Query());

        var search = _handler.Requests.Single();
        search.ApiKey.Should().Be(ApiKey);
        search.Bearer.Should().BeNull();
    }

    // The API asks every consumer to identify itself, and rejects requests that
    // do not.
    [Fact]
    public async Task Every_request_identifies_the_client()
    {
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(ApiKeyOnly()).SearchAsync(Query());

        _handler.Requests.Single().UserAgent.Should().Contain("Vora");
    }

    [Fact]
    public async Task Blank_credentials_in_account_mode_still_search_anonymously()
    {
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(Account(username: null)).SearchAsync(Query());

        _handler.Requests.Should().NotContain(r => r.Path.Contains("login"));
        _handler.Requests.Single().Bearer.Should().BeNull();
    }

    // --- Account mode --------------------------------------------------------

    [Fact]
    public async Task Account_mode_signs_in_then_carries_the_bearer()
    {
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-1", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(Account()).SearchAsync(Query());

        _handler.Requests.Should().HaveCount(2);
        _handler.Requests[0].Path.Should().Contain("login");
        _handler.Requests[1].Bearer.Should().Be("jwt-1");
        // The key identifies the consumer in both modes; the bearer only says
        // whose quota to bill.
        _handler.Requests[1].ApiKey.Should().Be(ApiKey);
    }

    // Login answers with the host this account should use — a VIP account gets a
    // different one — and later calls have to go there.
    [Fact]
    public async Task The_host_returned_by_login_is_used_afterwards()
    {
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-1", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(Account()).SearchAsync(Query());

        _handler.Requests[0].Host.Should().Be("api.opensubtitles.com");
        _handler.Requests[1].Host.Should().Be("vip-api.opensubtitles.com");
    }

    [Fact]
    public async Task The_token_is_reused_rather_than_signing_in_every_time()
    {
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-1", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        var provider = NewProvider(Account());
        await provider.SearchAsync(Query());
        await provider.SearchAsync(Query());

        _handler.Requests.Count(r => r.Path.Contains("login")).Should().Be(1);
    }

    // A token outlives most sessions but not all of them. An expiry that forced
    // the admin to re-save settings would look like the feature breaking at
    // random, so a 401 is answered by signing in again and retrying once.
    [Fact]
    public async Task An_expired_token_triggers_one_re_login_and_a_retry()
    {
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-old", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.Unauthorized, "");
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-new", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.OK, EmptySearchBody);

        await NewProvider(Account()).SearchAsync(Query());

        _handler.Requests.Count(r => r.Path.Contains("login")).Should().Be(2);
        _handler.Requests.Last().Bearer.Should().Be("jwt-new");
    }

    [Fact]
    public async Task A_download_after_re_login_still_carries_its_body()
    {
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-old", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.Unauthorized, "");
        _handler.Enqueue(HttpStatusCode.OK, LoginBody("jwt-new", VipBaseUrl));
        _handler.Enqueue(HttpStatusCode.OK, """{"link":"https://cdn.example/f.srt","file_name":"f.srt","remaining":40}""");
        _handler.Enqueue(HttpStatusCode.OK, "1\nsubtitle\n");

        await NewProvider(Account()).DownloadAsync("9001");

        var retried = _handler.Requests.Single(r => r.Path.Contains("download") && r.Bearer == "jwt-new");
        retried.Body.Should().Contain("9001");
    }

    [Fact]
    public async Task A_rejected_sign_in_leaves_the_search_empty_rather_than_throwing()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized, """{"message":"nope"}""");

        var results = await NewProvider(Account()).SearchAsync(Query());

        results.Should().BeEmpty();
    }

    // --- Quota ---------------------------------------------------------------

    // The viewer has to be told the limit is spent; a null return would surface
    // as a generic failure and leave them retrying against a wall that will not
    // move until the quota resets.
    [Fact]
    public async Task An_exhausted_quota_is_raised_as_a_provider_error()
    {
        _handler.Enqueue(HttpStatusCode.NotAcceptable, """{"message":"You have downloaded your allowed subtitles for today."}""");

        var act = async () => await NewProvider(ApiKeyOnly()).DownloadAsync("9001");

        var thrown = await act.Should().ThrowAsync<SubtitleProviderException>();
        thrown.Which.IsQuotaExhausted.Should().BeTrue();
        thrown.Which.Message.Should().Contain("allowed subtitles");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotAcceptable, null, null, null)]
    [InlineData(HttpStatusCode.TooManyRequests, null, null, null)]
    // A 200 can still refuse: remaining is zero and no link comes back.
    [InlineData(HttpStatusCode.OK, 0, null, null)]
    public void Every_shape_of_refusal_reads_as_out_of_downloads(HttpStatusCode status, int? remaining, string? message, string? link)
    {
        OpenSubtitlesSubtitleProvider.DescribeQuotaFailure(status, remaining, message, link).Should().NotBeNull();
    }

    [Fact]
    public void A_successful_download_is_not_mistaken_for_a_refusal()
    {
        OpenSubtitlesSubtitleProvider.DescribeQuotaFailure(HttpStatusCode.OK, 40, null, "https://cdn.example/f.srt")
            .Should().BeNull();
    }

    // Zero remaining alongside a real link is the LAST allowed download, not a
    // refusal — treating it as one would throw away a subtitle already paid for.
    [Fact]
    public void The_last_download_of_the_day_still_succeeds()
    {
        OpenSubtitlesSubtitleProvider.DescribeQuotaFailure(HttpStatusCode.OK, 0, null, "https://cdn.example/f.srt")
            .Should().BeNull();
    }

    [Fact]
    public void The_providers_own_wording_is_preferred_when_it_gives_one()
    {
        OpenSubtitlesSubtitleProvider.DescribeQuotaFailure(HttpStatusCode.NotAcceptable, 0, "Quota exceeded, resets at 00:00 UTC", null)
            .Should().Be("Quota exceeded, resets at 00:00 UTC");
    }

    // --- Base URL ------------------------------------------------------------

    [Theory]
    [InlineData("vip-api.opensubtitles.com", "https://vip-api.opensubtitles.com/api/v1/")]
    [InlineData("https://vip-api.opensubtitles.com", "https://vip-api.opensubtitles.com/api/v1/")]
    [InlineData("https://vip-api.opensubtitles.com/api/v1/", "https://vip-api.opensubtitles.com/api/v1/")]
    public void A_returned_host_becomes_a_usable_base_address(string returned, string expected)
    {
        OpenSubtitlesSubtitleProvider.NormalizeBaseUrl(returned).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_login_that_names_no_host_keeps_the_default(string? returned)
    {
        OpenSubtitlesSubtitleProvider.NormalizeBaseUrl(returned).Should().Be(OpenSubtitlesSubtitleProvider.DefaultBaseUrl);
    }

    private static string LoginBody(string token, string baseUrl) =>
        $$"""{"token":"{{token}}","base_url":"{{baseUrl}}","status":200}""";

    private sealed record RecordedRequest(string Path, string Host, string? ApiKey, string? Bearer, string? UserAgent, string Body);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<RecordedRequest> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.RequestUri.Host,
                request.Headers.TryGetValues("Api-Key", out var keys) ? keys.FirstOrDefault() : null,
                request.Headers.Authorization?.Parameter,
                request.Headers.UserAgent.ToString(),
                body));

            var (status, responseBody) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, string.Empty);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
