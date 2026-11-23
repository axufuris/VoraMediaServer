using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
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
    private readonly Guid _otherProfileId = Guid.NewGuid();
    private readonly TunerRegistry _tuners = new();
    private int _accepted;
    private bool _hold;
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
        await _coordinator.DetachAsync(_profileId);
        await _coordinator.DetachAsync(_otherProfileId);
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
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().ContainSingle("the session encodes, so it holds a slot").Which.OnGpu.Should().BeFalse();
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
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().ContainSingle();
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
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().BeEmpty("a copy takes no slot");
    }

    [Fact]
    public async Task A_channel_that_needs_an_encode_is_turned_away_when_the_server_is_running_as_many_as_it_may()
    {
        var channel = Serve(DummyStream(), new LiveVideoFormat("h264", "High 10", 51, 360, 25, false));
        var manager = Manager(new ServerSetting { UseHardwareAcceleration = false, MaxCpuTranscodes = 1 }, new Running(new RunningEncode(OnGpu: false)));

        var start = () => manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);

        await start.Should().ThrowAsync<EncodeLimitReachedException>().WithMessage("Maximum CPU transcodes reached.*");
        _coordinator.KindOfViewer(_profileId).Should().BeNull("nothing was started for the viewer who was turned away");
    }

    [Fact]
    public async Task A_channel_the_player_can_take_is_not_turned_away_by_the_encode_limit()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var known = new LiveVideoFormat("h264", "High", 41, 360, 25, false);
        var channel = Serve(await SampleAsync("high", "yuv420p", "4.1", cancellationToken), known);
        var manager = Manager(new ServerSetting { UseHardwareAcceleration = false, MaxCpuTranscodes = 1 }, new Running(new RunningEncode(OnGpu: false)));

        var url = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);

        url.Should().NotBeNull();
    }

    private static readonly LiveVideoFormat High10At1080 = new("h264", "High 10", 51, 1080, 25, false);

    [Fact]
    public async Task Two_viewers_of_one_channel_share_one_session_one_encode_and_one_tuner()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), new LiveVideoFormat("h264", "High 10", 51, 360, 25, false), hold: true);
        var manager = Manager();

        var first = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);
        var second = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _otherProfileId, Roku);

        second.Should().Be(first);
        _accepted.Should().Be(1, "one connection to the tuner serves both viewers");
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().ContainSingle("one encode, not one per viewer");
        _coordinator.SharedSessions(channel.Id, LiveSessionKind.Timeshift).Should().ContainSingle().Which.Viewers.Should().Be(2);
        (await SessionFormatAsync(second, cancellationToken)).Should().BeEquivalentTo(new { Codec = "h264", Profile = "High", Level = (int?)41 });

        await manager.StopTimeshiftSessionAsync(_profileId);

        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1, "the other viewer is still watching");
        _coordinator.SharedSessions(channel.Id, LiveSessionKind.Timeshift).Should().ContainSingle().Which.Viewers.Should().Be(1);

        await manager.StopTimeshiftSessionAsync(_otherProfileId);

        _tuners.ActiveCount(channel.PlaylistId).Should().Be(0, "the last viewer leaving frees the tuner");
        _coordinator.SharedSessions(channel.Id, LiveSessionKind.Timeshift).Should().BeEmpty();
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_player_that_cannot_take_the_shared_encode_gets_one_of_its_own()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), new LiveVideoFormat("h264", "High 10", 51, 360, 25, false), hold: true);
        var manager = Manager();
        var levelFour = new LiveVideoLimits(["h264"], 40, ["high"]);

        var first = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);
        var second = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _otherProfileId, levelFour);

        second.Should().NotBe(first);
        _accepted.Should().Be(2);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(2);
        _coordinator.RunningEncodes(Guid.NewGuid()).Should().HaveCount(2);
        (await SessionFormatAsync(second, cancellationToken)).Should().BeEquivalentTo(new { Level = (int?)40 });
    }

    [Fact]
    public async Task A_phone_joins_the_encode_a_tv_started()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), High10At1080, hold: true);
        var manager = Manager();

        var tv = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);
        var phone = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _otherProfileId, Roku with { MaxHeight = 720 });

        phone.Should().Be(tv);
        _accepted.Should().Be(1);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1);
    }

    [Fact]
    public async Task A_tv_does_not_join_the_smaller_picture_a_phone_started()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), High10At1080, hold: true);
        var manager = Manager();

        var phone = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku with { MaxHeight = 720 });
        var tv = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _otherProfileId, Roku);

        tv.Should().NotBe(phone);
        _accepted.Should().Be(2);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(2);
    }

    [Fact]
    public async Task A_session_whose_ffmpeg_has_died_is_replaced_not_joined_and_its_tuner_is_freed()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && FfmpegTool.IsAvailable(), "Finds ffmpeg's command line under /proc, so it needs Linux and ffmpeg.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var channel = Serve(await SampleAsync("high10", "yuv420p10le", "5.1", cancellationToken), new LiveVideoFormat("h264", "High 10", 51, 360, 25, false), hold: true);
        var manager = Manager();
        var first = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _profileId, Roku);
        KillFfmpegWritingUnder(_root);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (_coordinator.SharedSessions(channel.Id, LiveSessionKind.Timeshift).Count > 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(100, cancellationToken);
        }
        _coordinator.SharedSessions(channel.Id, LiveSessionKind.Timeshift).Should().BeEmpty("the session's ffmpeg was killed");

        var second = await manager.StartTimeshiftSessionAsync(channel.Id, Guid.NewGuid(), _otherProfileId, Roku);

        second.Should().NotBeNull().And.NotBe(first);
        _tuners.ActiveCount(channel.PlaylistId).Should().Be(1, "the ended session's tuner was freed for the new one");
    }

    private static void KillFfmpegWritingUnder(string folder)
    {
        foreach (var process in Process.GetProcessesByName("ffmpeg"))
        {
            try
            {
                if (File.ReadAllText($"/proc/{process.Id}/cmdline").Contains(folder, StringComparison.Ordinal)) process.Kill();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
            {
            }
        }
    }

    private sealed class Running(params RunningEncode[] encodes) : IEncodeSource
    {
        public IReadOnlyList<RunningEncode> RunningEncodes(Guid except) => encodes;
    }

    // A channel that is turned away is never opened, so what it would have sent doesn't matter.
    private string DummyStream()
    {
        var path = Path.Combine(_root, "never-opened.ts");
        File.WriteAllBytes(path, [0x47]);
        return path;
    }

    // others: encodes running elsewhere on the server (a playback transcode, a recording being converted).
    private IptvManager Manager(ServerSetting? serverSettings = null, params IEncodeSource[] others)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(serverSettings ?? new ServerSetting { UseHardwareAcceleration = false });
        var slots = new EncodeSlots([_coordinator, .. others]);
        var signer = Substitute.For<IStreamingTokenSigner>();
        signer.Sign(default!, default!, default).ReturnsForAnyArgs("token");

        var users = Substitute.For<IUserManager>();
        users.GetUserAccountAsync(Arg.Any<Guid>()).Returns(new UserVM
        {
            HasAllIptvAccess = true,
            Profiles = new List<UserProfileVM> { new() { Id = _profileId, HasAllIptvAccess = true }, new() { Id = _otherProfileId, HasAllIptvAccess = true } }
        });
        var protector = Substitute.For<IIptvSecretProtector>();

        return new IptvManager(
            _repository,
            Substitute.For<IIptvEpgService>(),
            users,
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

    private async Task<LiveVideoFormat?> SessionFormatAsync(string? url, CancellationToken cancellationToken)
    {
        url.Should().NotBeNull();
        var parts = (url ?? string.Empty).Split('/');
        var segment = Path.Combine(_root, TimeshiftCoordinator.TimeshiftSubdirectory, parts[^3], parts[^2], "seg_000.ts");
        var video = new TimeshiftVideo(Substitute.For<ISystemSettingsRepository>(), Substitute.For<IHardwareCapabilityService>(), Substitute.For<IEncodeSlots>(), NullLogger<TimeshiftVideo>.Instance);
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

    private IptvChannel Serve(string sample, LiveVideoFormat? known, bool hold = false)
    {
        _hold = hold;
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
            Interlocked.Increment(ref _accepted);
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

    private async Task SendStreamAsync(TcpClient client, byte[] stream, CancellationToken cancellationToken)
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
                if (_hold) await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }
}
