using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Vora.Api.Tests.Infra;
using Vora.Application.Streaming;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Streaming;

namespace Vora.Api.Tests;

public sealed class PlayHeadEndpointTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private readonly VoraApiTestFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-play-head-" + Guid.NewGuid().ToString("N"));
    private readonly string _webRoot;
    private readonly string _movie;
    private readonly IStreamRepository _sessions = Substitute.For<IStreamRepository>();
    private readonly IStreamManager _streams = Substitute.For<IStreamManager>();
    private readonly ITranscodeService _transcoder = Substitute.For<ITranscodeService>();

    public PlayHeadEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
        _webRoot = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Vora</title>");
        _movie = Path.Combine(_root, "Arrival.mkv");
        File.WriteAllBytes(_movie, new byte[4321]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private (HttpClient Client, IStreamingTokenSigner Signer) Host()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseWebRoot(_webRoot);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStreamRepository>();
                services.AddSingleton(_sessions);
                services.RemoveAll<IStreamManager>();
                services.AddSingleton(_streams);
                services.RemoveAll<ITranscodeService>();
                services.AddSingleton(_transcoder);
            });
        });
        return (factory.CreateClient(), factory.Services.GetRequiredService<IStreamingTokenSigner>());
    }

    private Guid Session(string strategy)
    {
        var id = Guid.NewGuid();
        _sessions.GetSessionAsync(id).Returns(new StreamSession { Id = id, MediaItemId = Guid.NewGuid(), Strategy = strategy });
        _streams.GetPlayableFilePathAsync(id).Returns(_movie);
        return id;
    }

    private static HttpRequestMessage Head(string url) => new(HttpMethod.Head, url);

    [Fact]
    public async Task A_head_on_a_direct_play_url_answers_with_the_files_headers_and_no_body()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, signer) = Host();
        var id = Session("DirectPlay");
        var token = signer.Sign(StreamManager.PlayTokenScope, id.ToString(), TimeSpan.FromMinutes(5));

        var response = await client.SendAsync(Head($"/api/streaming/play/{id}?t={token}"), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentLength.Should().Be(4321);
        response.Headers.AcceptRanges.Should().Contain("bytes");
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        (await response.Content.ReadAsByteArrayAsync(cancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_head_with_a_bad_token_is_not_found_as_a_get_is()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, _) = Host();
        var id = Session("DirectPlay");

        var head = await client.SendAsync(Head($"/api/streaming/play/{id}?t=not-a-token"), cancellationToken);
        var get = await client.GetAsync($"/api/streaming/play/{id}?t=not-a-token", cancellationToken);

        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_head_on_a_session_that_streams_through_hls_starts_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, signer) = Host();
        var id = Session("Transcode");
        var token = signer.Sign(StreamManager.PlayTokenScope, id.ToString(), TimeSpan.FromMinutes(5));

        var response = await client.SendAsync(Head($"/api/streaming/play/{id}?t={token}"), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Equal("GET");
        await _transcoder.DidNotReceive().StartTranscodeSessionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<PlaybackDecisionVM>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("GET", "/api/no-such-route")]
    [InlineData("HEAD", "/api/no-such-route")]
    [InlineData("GET", "/hubs/NoSuchHub")]
    public async Task An_unknown_api_or_hub_path_is_not_found_instead_of_the_web_app(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, _) = Host();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    [Fact]
    public async Task A_web_app_address_still_gets_the_web_app()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, _) = Host();

        var response = await client.GetAsync("/library/movies", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }
}
