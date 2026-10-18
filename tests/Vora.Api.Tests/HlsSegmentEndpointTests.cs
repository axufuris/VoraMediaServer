using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Vora.Api.Tests.Infra;
using Vora.Application.Streaming;

namespace Vora.Api.Tests;

public class HlsSegmentEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public HlsSegmentEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_segment_past_the_end_of_the_file_is_gone_so_the_player_can_finish_instead_of_retrying()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mediaItemId = Guid.NewGuid();
        var transcoder = Substitute.For<ITranscodeService>();
        transcoder.EnsureSegmentAvailableAsync(mediaItemId, 2, Arg.Any<CancellationToken>()).Returns(SegmentAvailability.PastTheEnd);
        transcoder.EnsureSegmentAvailableAsync(mediaItemId, 1, Arg.Any<CancellationToken>()).Returns(SegmentAvailability.NotReady);
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITranscodeService>();
            services.AddSingleton(transcoder);
        }));
        var token = factory.Services.GetRequiredService<IStreamingTokenSigner>().Sign(StreamManager.HlsTokenScope, mediaItemId.ToString(), TimeSpan.FromMinutes(5));
        var client = factory.CreateClient();

        var pastTheEnd = await client.GetAsync($"/api/streaming/hls/s/{token}/{mediaItemId}_2.ts", cancellationToken);
        var notReady = await client.GetAsync($"/api/streaming/hls/s/{token}/{mediaItemId}_1.ts", cancellationToken);

        pastTheEnd.StatusCode.Should().Be(HttpStatusCode.Gone);
        notReady.StatusCode.Should().Be(HttpStatusCode.NotFound);
        transcoder.DidNotReceive().NotifySegmentServed(Arg.Any<Guid>(), Arg.Any<int>());
    }
}
