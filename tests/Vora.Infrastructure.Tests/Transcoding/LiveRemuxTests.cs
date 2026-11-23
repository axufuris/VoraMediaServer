using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class LiveRemuxTests : IAsyncDisposable
{
    private const int ChunkBytes = 64 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-live-remux-" + Guid.NewGuid().ToString("N"));
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly TimeshiftCoordinator _coordinator = new(NullLogger<TimeshiftCoordinator>.Instance);
    private readonly TunerRegistry _tuners = new();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly List<string> _requests = new();
    private readonly CancellationTokenSource _serving = new();
    private TcpListener? _listener;

    public LiveRemuxTests()
    {
        Directory.CreateDirectory(_root);
        _repository.GetServerSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = _root });
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.StopAsync(_viewer);
        await _serving.CancelAsync();
        _listener?.Stop();
        _serving.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_raw_stream_is_remuxed_into_a_rolling_hls_window_with_the_provider_s_headers_and_cleaned_up_on_stop()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync(cancellationToken));
        var upstream = new IptvUpstream(channel.StreamUrl, new IptvRequestPolicy("RemuxAgent/1.0", "https://portal.example.com/", null));

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        url.Should().StartWith($"/api/streaming/hls/timeshift/").And.EndWith("/index.m3u8");
        var session = SessionFolder(url);
        File.Exists(Path.Combine(session, "index.m3u8")).Should().BeTrue();
        _coordinator.KindOf(_viewer).Should().Be(LiveSessionKind.Remux);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);
        lock (_requests)
        {
            _requests.Should().Contain(r => r.Contains("User-Agent: RemuxAgent/1.0", StringComparison.Ordinal) && r.Contains("Referer: https://portal.example.com/", StringComparison.Ordinal));
        }

        await WaitForEndAsync(session, cancellationToken);
        Directory.GetFiles(session, "seg_*.ts").Length.Should().BeLessThanOrEqualTo(IptvManager.LiveRemuxWindowSegments + 1);
        (await File.ReadAllTextAsync(Path.Combine(session, "index.m3u8"), cancellationToken)).Split("#EXTINF").Length.Should().Be(IptvManager.LiveRemuxWindowSegments + 1);

        await Manager().StopTimeshiftSessionAsync(_viewer);

        Directory.Exists(session).Should().BeFalse();
        _coordinator.IsActive(_viewer).Should().BeFalse();
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0);
    }

    [Fact]
    public async Task Watching_again_on_the_same_profile_replaces_the_remux_and_keeps_one_connection()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync(cancellationToken));
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var first = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);
        var second = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        Directory.Exists(SessionFolder(first)).Should().BeFalse();
        Directory.Exists(SessionFolder(second)).Should().BeTrue();
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);

        var evicted = await _coordinator.EvictStaleSessionsAsync(TimeSpan.FromHours(1), TimeSpan.FromHours(6), remuxMaxIdle: TimeSpan.Zero);
        foreach (var viewer in evicted) _tuners.Release(IptvManager.TimeshiftLeaseKey(viewer));

        evicted.Should().Equal(_viewer);
        Directory.Exists(SessionFolder(second)).Should().BeFalse();
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0);
    }

    [Fact]
    public async Task An_over_the_air_mpeg2_channel_is_encoded_to_h264_for_a_player_that_sent_no_limits()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await Mpeg2SampleAsync(cancellationToken));
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        var segment = Path.Combine(SessionFolder(url), "seg_000.ts");
        var format = await ReadFormatAsync(segment, cancellationToken);
        format.Should().NotBeNull();
        format!.Codec.Should().Be("h264");
        format.Interlaced.Should().BeFalse("the encode deinterlaces");
        await _repository.Received().UpdateChannelVideoFormatAsync(channel.Id, Arg.Is<LiveVideoFormat>(f => f.Codec == "mpeg2video"), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task An_mpeg2_channel_is_copied_for_a_player_that_says_it_takes_mpeg2()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await Mpeg2SampleAsync(cancellationToken));
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1, new LiveVideoLimits(["h264", "mpeg2video"], null, null));

        var format = await ReadFormatAsync(Path.Combine(SessionFolder(url), "seg_000.ts"), cancellationToken);
        format!.Codec.Should().Be("mpeg2video");
    }

    [Fact]
    public async Task A_remux_that_already_knows_the_channel_is_mpeg2_encodes_from_the_start()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await Mpeg2SampleAsync(cancellationToken));
        channel.VideoCodec = "mpeg2video";
        channel.VideoHeight = 480;
        channel.VideoFrameRate = 29.97;
        channel.VideoInterlaced = true;
        channel.VideoFormatSeenAt = DateTime.UtcNow;
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        var format = await ReadFormatAsync(Path.Combine(SessionFolder(url), "seg_000.ts"), cancellationToken);
        format!.Codec.Should().Be("h264");
        lock (_requests) _requests.Count.Should().Be(1, "a channel whose format is known is tuned once, not twice");
    }

    [Fact]
    public async Task A_channel_known_only_by_the_codec_its_lineup_named_is_tuned_once_and_comes_out_deinterlaced_h264()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await Mpeg2SampleAsync(cancellationToken));
        channel.VideoCodec = "mpeg2video";
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        var format = await ReadFormatAsync(Path.Combine(SessionFolder(url), "seg_000.ts"), cancellationToken);
        format!.Codec.Should().Be("h264");
        lock (_requests) _requests.Count.Should().Be(1, "the lineup said the channel is MPEG-2, so nothing had to be tuned to find out");
        await _repository.DidNotReceive().UpdateChannelVideoFormatAsync(channel.Id, Arg.Any<LiveVideoFormat>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task A_channel_known_only_by_codec_is_copied_for_a_player_that_takes_it_and_then_known_in_full()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await Mpeg2SampleAsync(cancellationToken));
        channel.VideoCodec = "mpeg2video";
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var url = await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1, new LiveVideoLimits(["h264", "mpeg2video"], null, null));

        (await ReadFormatAsync(Path.Combine(SessionFolder(url), "seg_000.ts"), cancellationToken))!.Codec.Should().Be("mpeg2video");
        await _repository.Received().UpdateChannelVideoFormatAsync(channel.Id, Arg.Is<LiveVideoFormat>(f => f.Codec == "mpeg2video" && f.Interlaced == true && f.Height == 480), Arg.Any<DateTime>());
    }

    private static Task<LiveVideoFormat?> ReadFormatAsync(string segment, CancellationToken cancellationToken) =>
        new TimeshiftVideo(Substitute.For<ISystemSettingsRepository>(), Substitute.For<IHardwareCapabilityService>(), Substitute.For<IEncodeSlots>(), NullLogger<TimeshiftVideo>.Instance)
            .ReadFormatAsync(segment, cancellationToken);

    private IptvManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { UseHardwareAcceleration = false });
        var signer = Substitute.For<IStreamingTokenSigner>();
        signer.Sign(default!, default!, default).ReturnsForAnyArgs("token");
        var protector = Substitute.For<IIptvSecretProtector>();
        var slots = new EncodeSlots([_coordinator]);

        return new IptvManager(
            _repository,
            Substitute.For<IIptvEpgService>(),
            Substitute.For<IUserManager>(),
            Substitute.For<ITaskQueueManager>(),
            _coordinator,
            _tuners,
            signer,
            new TimeshiftVideo(settings, Substitute.For<IHardwareCapabilityService>(), slots, NullLogger<TimeshiftVideo>.Instance),
            slots,
            Substitute.For<IIptvChannelLoader>(),
            new IptvUpstreamResolver(protector),
            protector,
            NullLogger<IptvManager>.Instance);
    }

    private string SessionFolder(string url)
    {
        var parts = url.Split('/');
        return Path.Combine(_root, TimeshiftCoordinator.TimeshiftSubdirectory, parts[^3], parts[^2]);
    }

    private static async Task WaitForEndAsync(string session, CancellationToken cancellationToken)
    {
        var playlist = Path.Combine(session, "index.m3u8");
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(playlist) && (await File.ReadAllTextAsync(playlist, cancellationToken)).Contains("#EXT-X-ENDLIST", StringComparison.Ordinal)) return;
            await Task.Delay(250, cancellationToken);
        }
    }

    private async Task<string> SampleAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "sample.ts");
        if (File.Exists(path)) return path;
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=48",
            "-f", "lavfi", "-i", "sine=duration=48",
            "-c:v", "libx264", "-preset", "veryfast", "-g", "50",
            "-c:a", "aac", path);
        return path;
    }

    [Fact]
    public async Task A_tuner_that_answers_503_is_reported_as_all_tuners_in_use_and_frees_the_lease()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var channel = ServeStatus(503);
        channel.Playlist = new IptvPlaylist { Name = "Antenna", SourceType = IptvSourceType.HdHomeRun, ServerUrl = "http://127.0.0.1" };
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var act = async () => await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        await act.Should().ThrowAsync<TunerLimitReachedException>();
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0);
        _coordinator.IsActive(_viewer).Should().BeFalse();
    }

    [Fact]
    public async Task A_503_from_an_ordinary_provider_is_a_dead_stream_not_a_busy_tuner()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var channel = ServeStatus(503);
        var upstream = new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None);

        var act = async () => await Manager().StartLiveRemuxAsync(channel, upstream, _viewer, connectionLimit: 1);

        await act.Should().ThrowAsync<IptvSourceException>();
    }

    // What an antenna tuner sends: interlaced MPEG-2 video and AC-3 audio in a transport stream.
    private async Task<string> Mpeg2SampleAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, "mpeg2.ts");
        if (File.Exists(path)) return path;
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=720x480:rate=30000/1001:duration=48",
            "-f", "lavfi", "-i", "sine=duration=48",
            "-c:v", "mpeg2video", "-flags", "+ilme+ildct", "-top", "1", "-g", "15", "-b:v", "3M",
            "-c:a", "ac3", path);
        return path;
    }

    private IptvChannel Serve(string sample)
    {
        var stream = File.ReadAllBytes(sample);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync(_listener, stream, _serving.Token);

        var channel = new IptvChannel
        {
            ExternalChannelId = "news",
            Name = "News",
            StreamUrl = $"http://127.0.0.1:{port}/live.ts",
            PlaylistId = Guid.NewGuid()
        };
        _repository.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        return channel;
    }

    private IptvChannel ServeStatus(int status)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptStatusAsync(_listener, status, _serving.Token);

        var channel = new IptvChannel
        {
            ExternalChannelId = "7.1",
            Name = "WABC-HD",
            StreamUrl = $"http://127.0.0.1:{port}/auto/v7.1",
            PlaylistId = Guid.NewGuid()
        };
        _repository.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        return channel;
    }

    private static async Task AcceptStatusAsync(TcpListener listener, int status, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            using (client)
            {
                try
                {
                    var network = client.GetStream();
                    await ReadRequestAsync(network, cancellationToken);
                    await network.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Service Unavailable\r\nX-HDHomeRun-Error: 805\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private async Task AcceptAsync(TcpListener listener, byte[] stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }
            _ = SendStreamAsync(client, stream, cancellationToken);
        }
    }

    private async Task SendStreamAsync(TcpClient client, byte[] stream, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var network = client.GetStream();
                var request = await ReadRequestAsync(network, cancellationToken);
                if (request is null) return;
                lock (_requests) _requests.Add(request);
                await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\n\r\n"), cancellationToken);
                for (var offset = 0; offset < stream.Length; offset += ChunkBytes)
                {
                    await network.WriteAsync(stream.AsMemory(offset, Math.Min(ChunkBytes, stream.Length - offset)), cancellationToken);
                    await Task.Delay(15, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<string?> ReadRequestAsync(NetworkStream network, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var received = 0;
        while (received < buffer.Length)
        {
            var read = await network.ReadAsync(buffer.AsMemory(received), cancellationToken);
            if (read == 0) return null;
            received += read;
            var text = Encoding.ASCII.GetString(buffer, 0, received);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text;
        }
        return null;
    }
}
