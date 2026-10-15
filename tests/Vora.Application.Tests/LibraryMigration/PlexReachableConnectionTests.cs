using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Plex;

namespace Vora.Application.Tests.LibraryMigration;

public class PlexReachableConnectionTests
{
    private const string ServerId = "f3a1c0de";
    private const string LocalHost = "192-168-0-10.abc123.plex.direct";
    private const string LocalIp = "192.168.0.10";
    private const string RemoteHost = "198-51-100-7.abc123.plex.direct";
    private const string RelayHost = "203-0-113-50.abc123.plex.direct";

    private const string Resources = """
    [{"name":"Home","provides":"server","clientIdentifier":"f3a1c0de","owned":true,"presence":true,"connections":[
      {"protocol":"https","address":"192.168.0.10","port":32400,"uri":"https://192-168-0-10.abc123.plex.direct:32400","local":true,"relay":false},
      {"protocol":"https","address":"198.51.100.7","port":32400,"uri":"https://198-51-100-7.abc123.plex.direct:32400","local":false,"relay":false},
      {"protocol":"https","address":"203.0.113.50","port":8443,"uri":"https://203-0-113-50.abc123.plex.direct:8443","local":false,"relay":true}]}]
    """;

    private const string Sections = """{"MediaContainer":{"size":1,"Directory":[{"key":"1","type":"movie","title":"Movies"}]}}""";

    private delegate Task<HttpResponseMessage> Answer(CancellationToken cancellationToken);

    private sealed class Network(Dictionary<string, Answer> byHost) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? string.Empty;
            if (host == "plex.tv") return Task.FromResult(Json(Resources));
            return byHost.TryGetValue(host, out var answer) ? answer(cancellationToken) : Refused(host)(cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Answer Answers() => _ => Task.FromResult(Json(Sections));

    private static Answer AnswersAfter(TimeSpan delay) => async cancellationToken =>
    {
        await Task.Delay(delay, cancellationToken);
        return Json(Sections);
    };

    private static Answer Refused(string host) => _ => Task.FromException<HttpResponseMessage>(
        new HttpRequestException(HttpRequestError.ConnectionError, $"Connection refused ({host}:32400)", new SocketException((int)SocketError.ConnectionRefused)));

    private static Answer DoesNotResolve(string host) => _ => Task.FromException<HttpResponseMessage>(
        new HttpRequestException(HttpRequestError.NameResolutionError, $"Name or service not known ({host}:32400)", new SocketException((int)SocketError.HostNotFound)));

    private static Answer NeverAnswers() => async cancellationToken =>
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return Json(Sections);
    };

    private static Answer Status(HttpStatusCode status) => _ => Task.FromResult(new HttpResponseMessage(status));

    private static PlexLibrarySyncProvider Provider(Dictionary<string, Answer> byHost)
    {
        var network = new Network(byHost);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(network, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(1) });
        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync("plex_library_sync", "client_identifier").Returns("vora-test-client");
        return new PlexLibrarySyncProvider(factory, settings, NullLogger<PlexLibrarySyncProvider>.Instance);
    }

    [Fact]
    public async Task When_the_local_address_is_refused_the_remote_one_is_used()
    {
        var provider = Provider(new()
        {
            [LocalHost] = Refused(LocalHost),
            [LocalIp] = Refused(LocalIp),
            [RemoteHost] = Answers(),
            [RelayHost] = Answers(),
        });

        var uri = await provider.FindReachableConnectionAsync(ServerId, "plex-token", TestContext.Current.CancellationToken);

        uri.Should().Be("https://198-51-100-7.abc123.plex.direct:32400");
    }

    [Fact]
    public async Task When_the_plex_direct_name_does_not_resolve_the_local_IP_is_used()
    {
        var provider = Provider(new()
        {
            [LocalHost] = DoesNotResolve(LocalHost),
            [LocalIp] = Answers(),
            [RemoteHost] = Answers(),
        });

        var uri = await provider.FindReachableConnectionAsync(ServerId, "plex-token", TestContext.Current.CancellationToken);

        uri.Should().Be("http://192.168.0.10:32400");
    }

    [Fact]
    public async Task The_local_address_wins_even_when_the_relay_answers_first()
    {
        var provider = Provider(new()
        {
            [LocalHost] = AnswersAfter(TimeSpan.FromMilliseconds(200)),
            [RelayHost] = Answers(),
        });

        var uri = await provider.FindReachableConnectionAsync(ServerId, "plex-token", TestContext.Current.CancellationToken);

        uri.Should().Be("https://192-168-0-10.abc123.plex.direct:32400");
    }

    [Fact]
    public async Task When_nothing_answers_the_error_names_every_address_and_why()
    {
        var provider = Provider(new()
        {
            [LocalHost] = DoesNotResolve(LocalHost),
            [LocalIp] = Refused(LocalIp),
            [RemoteHost] = NeverAnswers(),
            [RelayHost] = Refused(RelayHost),
        });

        var find = () => provider.FindReachableConnectionAsync(ServerId, "plex-token", TestContext.Current.CancellationToken);

        var error = (await find.Should().ThrowAsync<RemoteServerUnreachableException>()).Which;
        error.Message.Should().StartWith("Vora couldn't reach the Plex server \"Home\".")
            .And.Contain("the local address 192.168.0.10:32400 (its name did not resolve")
            .And.Contain("the local address 192.168.0.10:32400 over plain HTTP (connection refused)")
            .And.Contain("the remote address 198.51.100.7:32400 (no answer within")
            .And.Contain("the Plex relay (connection refused)")
            .And.Contain("from inside the container");
    }

    [Fact]
    public async Task A_server_that_turns_the_account_away_is_named_as_refusing_it()
    {
        var provider = Provider(new()
        {
            [LocalHost] = Status(HttpStatusCode.Unauthorized),
            [LocalIp] = Status(HttpStatusCode.Unauthorized),
            [RemoteHost] = Status(HttpStatusCode.Unauthorized),
            [RelayHost] = Status(HttpStatusCode.Unauthorized),
        });

        var find = () => provider.FindReachableConnectionAsync(ServerId, "plex-token", TestContext.Current.CancellationToken);

        (await find.Should().ThrowAsync<RemoteServerUnreachableException>()).Which.Message
            .Should().Contain("the local address 192.168.0.10:32400 (Plex refused this account, status 401)");
    }

    [Fact]
    public async Task A_server_Plex_no_longer_lists_asks_for_the_server_to_be_picked_again()
    {
        var provider = Provider(new());

        var find = () => provider.FindReachableConnectionAsync("someone-else", "plex-token", TestContext.Current.CancellationToken);

        (await find.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Pick the server again");
    }
}
