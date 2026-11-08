using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Plex;

namespace Vora.Application.Tests.LibraryMigration;

public class PlexHomeUserSwitchTests
{
    private const string ServerId = "f3a1c0de";

    private sealed class PlexTv : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Switch { get; set; } = () => Json("""{"authToken":"kid-token"}""");
        public Dictionary<string, string> UserByToken { get; } = new()
        {
            ["owner-token"] = """{"uuid":"owner-uuid","title":"Andy"}""",
            ["kid-token"] = """{"uuid":"kid-uuid","title":"Kid"}""",
            ["stranger-token"] = """{"uuid":"stranger-uuid","title":"Stranger"}""",
        };
        public Dictionary<string, string> ResourcesByToken { get; } = new()
        {
            ["owner-token"] = $$"""[{"name":"Home","provides":"server","clientIdentifier":"{{ServerId}}","owned":true,"accessToken":"owner-server-token"}]""",
            ["kid-token"] = $$"""[{"name":"Home","provides":"server","clientIdentifier":"{{ServerId}}","owned":false,"accessToken":"kid-server-token"}]""",
        };
        public List<string> Switches { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var token = request.Headers.TryGetValues("X-Plex-Token", out var values) ? values.Single() : string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/switch", StringComparison.Ordinal))
            {
                Switches.Add(request.RequestUri.PathAndQuery);
                return Task.FromResult(Switch());
            }
            if (path == "/api/v2/user")
            {
                return Task.FromResult(UserByToken.TryGetValue(token, out var user) ? Json(user) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            if (path == "/api/v2/resources")
            {
                return Task.FromResult(Json(ResourcesByToken.TryGetValue(token, out var resources) ? resources : "[]"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private readonly PlexTv _plexTv = new();

    private PlexLibrarySyncProvider Provider()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_plexTv, disposeHandler: false));
        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync("plex_library_sync", "client_identifier").Returns("vora-test-client");
        return new PlexLibrarySyncProvider(factory, settings, NullLogger<PlexLibrarySyncProvider>.Instance);
    }

    private Task<string> ResolveAsync(string? accountId, string? pin = null) =>
        Provider().ResolveServerTokenAsync("owner-token", accountId, pin, ServerId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_Plex_Home_user_is_read_with_their_own_token_for_the_server()
    {
        var token = await ResolveAsync("kid-uuid", pin: "1234");

        token.Should().Be("kid-server-token");
        _plexTv.Switches.Should().Equal("/api/v2/home/users/kid-uuid/switch?pin=1234");
    }

    [Fact]
    public async Task The_signed_in_account_is_read_without_switching()
    {
        (await ResolveAsync(null)).Should().Be("owner-server-token");
        (await ResolveAsync("owner-uuid")).Should().Be("owner-server-token");
        _plexTv.Switches.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wrong_PIN_fails_instead_of_reading_the_signed_in_history()
    {
        _plexTv.Switch = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var resolve = () => ResolveAsync("kid-uuid", pin: "0000");

        (await resolve.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*PIN is wrong or missing*");
    }

    [Fact]
    public async Task Any_other_refused_switch_fails_instead_of_reading_the_signed_in_history()
    {
        _plexTv.Switch = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        var resolve = () => ResolveAsync("kid-uuid");

        (await resolve.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*status 404*nothing was imported*");
    }

    [Fact]
    public async Task A_switch_with_no_sign_in_fails()
    {
        _plexTv.Switch = () => Json("{}");

        var resolve = () => ResolveAsync("kid-uuid");

        await resolve.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_switch_that_lands_on_someone_else_fails()
    {
        _plexTv.Switch = () => Json("""{"authToken":"stranger-token"}""");

        var resolve = () => ResolveAsync("kid-uuid");

        (await resolve.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*someone other than the Plex Home user picked*");
    }

    [Fact]
    public async Task A_Plex_user_who_cannot_see_the_server_fails()
    {
        _plexTv.ResourcesByToken["kid-token"] = "[]";

        var resolve = () => ResolveAsync("kid-uuid");

        (await resolve.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no access to that Plex server*");
    }

    [Fact]
    public async Task A_server_listed_without_its_own_token_is_read_with_the_users_token()
    {
        _plexTv.ResourcesByToken["kid-token"] = $$"""[{"name":"Home","provides":"server","clientIdentifier":"{{ServerId}}"}]""";

        (await ResolveAsync("kid-uuid")).Should().Be("kid-token");
    }
}
