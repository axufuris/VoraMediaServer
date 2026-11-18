using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.LibraryMigration;
using Vora.Application.Users;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Plex;

namespace Vora.Application.Tests.LibraryMigration;

public class PlexSharedServerTests
{
    private const string ServerId = "f3a1c0de";
    private const string LocalIp = "192.168.0.10";
    private const string Sections = """{"MediaContainer":{"size":1,"Directory":[{"key":"1","type":"movie","title":"Movies"}]}}""";

    private static string Resources(bool owned, string? accessToken)
    {
        var access = accessToken is null ? string.Empty : $",\"accessToken\":\"{accessToken}\"";
        return $$"""
        [{"name":"Taurus","provides":"server","clientIdentifier":"{{ServerId}}","owned":{{(owned ? "true" : "false")}},"sourceTitle":"Owner","presence":true{{access}},"connections":[
          {"protocol":"http","address":"{{LocalIp}}","port":32400,"uri":"http://{{LocalIp}}:32400","local":true,"relay":false}]}]
        """;
    }

    private sealed class Network(string resources, string tokenTheServerAccepts) : HttpMessageHandler
    {
        public List<string?> TokensSeenByTheServer { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? string.Empty;
            if (host == "plex.tv") return Task.FromResult(Json(resources));
            if (host != LocalIp)
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"));
            }

            var token = request.Headers.TryGetValues("X-Plex-Token", out var values) ? values.FirstOrDefault() : null;
            TokensSeenByTheServer.Add(token);
            return Task.FromResult(token == tokenTheServerAccepts ? Json(Sections) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static PlexLibrarySyncProvider Provider(Network network)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(network, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(1) });
        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync("plex_library_sync", "client_identifier").Returns("vora-test-client");
        return new PlexLibrarySyncProvider(factory, settings, NullLogger<PlexLibrarySyncProvider>.Instance);
    }

    [Fact]
    public async Task A_server_shared_with_the_account_is_reached_with_the_servers_own_access_token()
    {
        var network = new Network(Resources(owned: false, accessToken: "server-token"), tokenTheServerAccepts: "server-token");

        var uri = await Provider(network).FindReachableConnectionAsync(ServerId, "account-token", TestContext.Current.CancellationToken);

        uri.Should().Be($"http://{LocalIp}:32400");
        network.TokensSeenByTheServer.Should().OnlyContain(token => token == "server-token");
    }

    [Fact]
    public async Task A_server_that_lists_no_access_token_is_reached_with_the_account_token()
    {
        var network = new Network(Resources(owned: true, accessToken: null), tokenTheServerAccepts: "account-token");

        var uri = await Provider(network).FindReachableConnectionAsync(ServerId, "account-token", TestContext.Current.CancellationToken);

        uri.Should().Be($"http://{LocalIp}:32400");
    }

    [Fact]
    public async Task When_the_server_refuses_the_account_the_error_says_what_to_check()
    {
        var network = new Network(Resources(owned: false, accessToken: "server-token"), tokenTheServerAccepts: "someone-elses-token");

        var find = () => Provider(network).FindReachableConnectionAsync(ServerId, "account-token", TestContext.Current.CancellationToken);

        var error = (await find.Should().ThrowAsync<RemoteServerUnreachableException>()).Which;
        error.Message.Should().Contain("Plex refused this account, status 401")
            .And.Contain("The server did answer but refused this Plex account")
            .And.Contain("has to share it with them");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_at_all_does_not_blame_the_account()
    {
        var resources = Resources(owned: false, accessToken: "server-token").Replace(LocalIp, "192.168.0.99", StringComparison.Ordinal);
        var network = new Network(resources, tokenTheServerAccepts: "server-token");

        var find = () => Provider(network).FindReachableConnectionAsync(ServerId, "account-token", TestContext.Current.CancellationToken);

        var error = (await find.Should().ThrowAsync<RemoteServerUnreachableException>()).Which;
        error.Message.Should().NotContain("refused this Plex account");
    }

    [Fact]
    public async Task The_library_list_is_read_with_the_server_token_the_provider_resolves()
    {
        var provider = Substitute.For<ILibrarySyncProvider>();
        provider.Id.Returns("plex_library_sync");
        provider.FindReachableConnectionAsync(ServerId, "account-token", Arg.Any<CancellationToken>()).Returns("http://192.168.0.10:32400");
        provider.ResolveServerTokenAsync("account-token", null, null, ServerId, Arg.Any<CancellationToken>()).Returns("server-token");
        provider.ListLibrariesAsync("http://192.168.0.10:32400", "server-token", Arg.Any<CancellationToken>())
            .Returns([new RemoteLibraryDto { Key = "1", Name = "Movies", Kind = RemoteLibraryKind.Movie }]);
        var manager = new LibraryMigrationManager([provider], Substitute.For<IUserRepository>(), Substitute.For<ILibraryMigrationJobRunner>(), NullLogger<LibraryMigrationManager>.Instance);

        var libraries = await manager.ListLibrariesAsync("plex_library_sync", "account-token", ServerId, TestContext.Current.CancellationToken);

        libraries.Should().ContainSingle().Which.Name.Should().Be("Movies");
        await provider.DidNotReceive().ListLibrariesAsync(Arg.Any<string>(), "account-token", Arg.Any<CancellationToken>());
    }
}
