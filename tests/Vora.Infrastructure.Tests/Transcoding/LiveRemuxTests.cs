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

    private IptvManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { UseHardwareAcceleration = false });
        var signer = Substitute.For<IStreamingTokenSigner>();
        signer.Sign(default!, default!, default).ReturnsForAnyArgs("token");
        var protector = Substitute.For<IIptvSecretProtector>();

        return new IptvManager(
            _repository,
            Substitute.For<IIptvEpgService>(),
            Substitute.For<IUserManager>(),
            Substitute.For<ITaskQueueManager>(),
            _coordinator,
            _tuners,
            signer,
            new TimeshiftVideo(settings, Substitute.For<IHardwareCapabilityService>(), NullLogger<TimeshiftVideo>.Instance),
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
