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

public sealed class TimeshiftClientLimitsTests : IAsyncDisposable
{
    private const int ChunkBytes = 64 * 1024;
    private const int ChunkDelayMs = 15;

    private static readonly LiveVideoLimits Roku = new(["h264", "hevc", "mpeg4"], 42, ["baseline", "main", "high"]);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-timeshift-limits-" + Guid.NewGuid().ToString("N"));
    private readonly IIptvRepository _repository = Substitute.For<IIptvRepository>();
    private readonly TimeshiftCoordinator _coordinator = new(NullLogger<TimeshiftCoordinator>.Instance);
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly CancellationTokenSource _serving = new();
    private TcpListener? _listener;

    public TimeshiftClientLimitsTests()
    {
        Directory.CreateDirectory(_root);
        _repository.GetServerSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = _root });
        _repository.GetTunerProfileByPlaylistIdAsync(Arg.Any<Guid>()).Returns((IptvTunerProfile?)null);
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.StopAsync(_profileId);
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
    public async Task A_channel_seen_for_the_first_time_is_copied_then_encoded_before_the_player_is_given_it()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), known: null);

        var url = await Manager().StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);

        await _repository.Received(1).UpdateChannelVideoFormatAsync(channel.Id, Arg.Is<LiveVideoFormat>(f => f.Profile == "High 10" && f.Level == 51), Arg.Any<DateTime>());
        (await SessionFormatAsync(url, cancellationToken)).Should().BeEquivalentTo(new { Codec = "h264", Profile = "High", Level = (int?)41 });
    }

    [Fact]
    public async Task A_channel_known_to_be_beyond_the_player_is_encoded_from_the_start()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), new LiveVideoFormat("h264", "High 10", 51, 360, 25, false));

        var url = await Manager().StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);

        await _repository.DidNotReceiveWithAnyArgs().UpdateChannelVideoFormatAsync(default, default!, default);
        (await SessionFormatAsync(url, cancellationToken)).Should().BeEquivalentTo(new { Profile = "High", Level = (int?)41 });
    }

    [Fact]
    public async Task A_player_that_sends_no_limits_gets_the_channel_as_it_is_and_the_server_still_learns_its_format()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), known: null);

        var url = await Manager().StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, limits: null);

        await _repository.Received(1).UpdateChannelVideoFormatAsync(channel.Id, Arg.Is<LiveVideoFormat>(f => f.Profile == "High 10"), Arg.Any<DateTime>());
        (await SessionFormatAsync(url, cancellationToken)).Should().BeEquivalentTo(new { Profile = "High 10", Level = (int?)51 });
    }

    [Fact]
    public async Task A_known_channel_the_player_can_take_is_copied_without_an_encode()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var known = new LiveVideoFormat("h264", "High", 41, 360, 25, false);
        var channel = Serve(await SampleAsync("high", "yuv420p", "4.1", cancellationToken), known);

        var url = await Manager().StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);

        await _repository.DidNotReceiveWithAnyArgs().UpdateChannelVideoFormatAsync(default, default!, default);
        (await SessionFormatAsync(url, cancellationToken)).Should().Be(known);
    }

    private IptvManager Manager()
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { UseHardwareAcceleration = false });
        var signer = Substitute.For<IStreamingTokenSigner>();
        signer.Sign(default!, default!, default).ReturnsForAnyArgs("token");

        return new IptvManager(
            _repository,
            Substitute.For<IIptvEpgService>(),
            Substitute.For<IUserManager>(),
            Substitute.For<ITaskQueueManager>(),
            _coordinator,
            new TunerRegistry(),
            Substitute.For<IHttpClientFactory>(),
            signer,
            new TimeshiftVideo(settings, Substitute.For<IHardwareCapabilityService>(), NullLogger<TimeshiftVideo>.Instance),
            NullLogger<IptvManager>.Instance);
    }

    private async Task<LiveVideoFormat?> SessionFormatAsync(string? url, CancellationToken cancellationToken)
    {
        url.Should().NotBeNull();
        var sessionId = (url ?? string.Empty).Split('/')[^2];
        var segment = Path.Combine(_root, TimeshiftCoordinator.TimeshiftSubdirectory, _profileId.ToString(), sessionId, "seg_000.ts");
        var video = new TimeshiftVideo(Substitute.For<ISystemSettingsRepository>(), Substitute.For<IHardwareCapabilityService>(), NullLogger<TimeshiftVideo>.Instance);
        return await video.ReadFormatAsync(segment, cancellationToken);
    }

    private async Task<string> SampleAsync(string profile, string pixelFormat, string level, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, $"{profile}.ts");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=12",
            "-f", "lavfi", "-i", "sine=duration=12",
            "-c:v", "libx264", "-preset", "veryfast", "-profile:v", profile, "-level", level, "-pix_fmt", pixelFormat,
            "-c:a", "aac", path);
        return path;
    }

    private IptvChannel Serve(string sample, LiveVideoFormat? known)
    {
        var stream = File.ReadAllBytes(sample);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync(_listener, stream, _serving.Token);

        var channel = new IptvChannel
        {
            ExternalChannelId = "action24",
            Name = "Action 24 Not 24/7",
            StreamUrl = $"http://127.0.0.1:{port}/live.ts",
            PlaylistId = Guid.NewGuid(),
            VideoCodec = known?.Codec,
            VideoProfile = known?.Profile,
            VideoLevel = known?.Level,
            VideoHeight = known?.Height,
            VideoFrameRate = known?.FrameRate,
            VideoInterlaced = known?.Interlaced,
        };
        _repository.GetChannelByIdAsync(channel.Id).Returns(channel);
        return channel;
    }

    private static async Task AcceptAsync(TcpListener listener, byte[] stream, CancellationToken cancellationToken)
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

    private static async Task<bool> ReadRequestAsync(NetworkStream network, CancellationToken cancellationToken)
    {
        var request = new byte[8192];
        var received = 0;
        while (received < request.Length)
        {
            var read = await network.ReadAsync(request.AsMemory(received), cancellationToken);
            if (read == 0) return false;
            received += read;
            if (Encoding.ASCII.GetString(request, 0, received).Contains("\r\n\r\n", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static async Task SendStreamAsync(TcpClient client, byte[] stream, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                var network = client.GetStream();
                if (!await ReadRequestAsync(network, cancellationToken)) return;
                await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\n\r\n"), cancellationToken);
                for (var offset = 0; offset < stream.Length; offset += ChunkBytes)
                {
                    await network.WriteAsync(stream.AsMemory(offset, Math.Min(ChunkBytes, stream.Length - offset)), cancellationToken);
                    await Task.Delay(ChunkDelayMs, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }
}
