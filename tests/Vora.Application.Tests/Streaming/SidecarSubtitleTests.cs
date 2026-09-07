using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Auth;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

// Text subtitles are stripped from the HLS output on purpose, so a non-burn-in
// subtitle only reaches the player as a sidecar WebVTT. Extraction can take
// minutes on a large remux, so /start never waits for it: a cached file is
// answered immediately, a miss starts background work and answers null. These
// pin that split, and that the URL is one the HLS file route will serve.
public class SidecarSubtitleTests
{
    private const string TempDir = "/transcode";
    private const int SubtitleStreamIndex = 3;
    private const int SubtitleOrdinal = 1;

    private static readonly Guid MediaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PartId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SubtitleTrackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ExtraId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static string CachedName => $"{PartId}_{SubtitleTrackId}.vtt";

    private readonly IStreamRepository _repo = Substitute.For<IStreamRepository>();
    private readonly IBestPathDecisionManager _decisions = Substitute.For<IBestPathDecisionManager>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ISubtitleExtractionService _extractor = Substitute.For<ISubtitleExtractionService>();
    private readonly StreamingTokenSigner _signer = new(
        Options.Create(new JwtOptions { Issuer = "t", Audience = "t", SecretKey = "test-secret-key-must-be-long-enough-for-hmac" }),
        NullLogger<StreamingTokenSigner>.Instance);

    private StreamManager NewManager() => new(
        _repo,
        _decisions,
        _settings,
        _signer,
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITranscodeService>(),
        _extractor,
        Options.Create(new StoragePathsOptions()));

    private void Arrange(bool subtitleSelected, bool burnIn, bool cached, Guid? extraId = null)
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = TempDir });
        _repo.ResolvePlayableMediaIdAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns(MediaId);
        _repo.GetClientDeviceAsync(Arg.Any<string>()).Returns(new ClientDevice { Id = Guid.NewGuid(), DeviceId = "device-1", LastIpAddress = "192.168.0.10" });
        _repo.GetMediaStreamInfoAsync(Arg.Any<Guid>()).Returns(new MediaStreamInfoDto
        {
            Id = MediaId,
            Parts = new List<MediaPartStreamInfoDto> { new() { Id = PartId } }
        });
        _repo.GetMediaExtraAsync(Arg.Any<Guid>()).Returns(new MediaExtra { Id = ExtraId, MediaItemId = MediaId, Title = "Trailer" });
        _repo.GetExtraStreamInfoAsync(Arg.Any<Guid>()).Returns(new MediaStreamInfoDto
        {
            Id = ExtraId,
            Parts = new List<MediaPartStreamInfoDto> { new() { Id = PartId } }
        });

        _decisions.DetermineBestPathAsync(
            Arg.Any<ClientDevice>(), Arg.Any<MediaStreamInfoDto>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>())
            .Returns(new StreamDecisionDto
            {
                Strategy = StreamStrategy.Transcode,
                SelectedMediaPartId = PartId,
                SelectedSubtitleTrackId = subtitleSelected ? SubtitleTrackId : null,
                RequiresSubtitleBurnIn = burnIn,
                SubtitleStrategy = !subtitleSelected ? "None" : burnIn ? "BurnIn" : "DirectPlay",
            });

        _repo.CreateSessionAsync(Arg.Any<StreamSession>()).Returns(call =>
        {
            var session = call.Arg<StreamSession>();
            session.Id = SessionId;
            session.ExtraId = extraId;
            return session;
        });

        _repo.GetMediaPartForSessionAsync(SessionId).Returns(new MediaPart
        {
            Id = PartId,
            FilePath = "/media/movie.mkv",
            SubtitleTracks = new List<MediaSubtitleTrack>
            {
                new() { Id = Guid.NewGuid(), StreamIndex = 2, Codec = "subrip" },
                new() { Id = SubtitleTrackId, StreamIndex = SubtitleStreamIndex, Codec = "subrip" },
            }
        });

        _extractor.TryGetCachedWebVtt(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), out Arg.Any<string>())
            .Returns(call =>
            {
                call[3] = $"{call.ArgAt<Guid>(1)}_{call.ArgAt<Guid>(2)}.vtt";
                return cached;
            });

        _extractor.BeginExtractionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(Task.CompletedTask);
    }

    private Task<(StreamSession Session, string StreamUrl, string? SubtitleUrl)> StartAsync() =>
        NewManager().StartSessionAsync(MediaId, "device-1", Guid.NewGuid(), Guid.NewGuid(), 0);

    private Task NoExtractionStarted() => _extractor.DidNotReceive().BeginExtractionAsync(
        Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>());

    [Fact]
    public async Task A_cached_subtitle_is_returned_without_extracting()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: true);

        var result = await StartAsync();

        result.SubtitleUrl.Should().NotBeNullOrEmpty();
        await NoExtractionStarted();
    }

    // First selection for this file: the stream must start now, so the caller
    // gets no subtitle and the extraction runs behind it.
    [Fact]
    public async Task A_cache_miss_starts_extraction_and_answers_without_a_subtitle()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        result.StreamUrl.Should().NotBeNullOrEmpty();
        await _extractor.Received(1).BeginExtractionAsync(
            "/media/movie.mkv", SubtitleStreamIndex, SubtitleOrdinal, TempDir, PartId, SubtitleTrackId);
    }

    // The whole point of the change: a slow extraction used to hold /start open
    // for its full timeout and the client gave up. Starting a session must not
    // wait on the extraction task at all.
    [Fact]
    public async Task Start_does_not_wait_for_the_extraction_to_finish()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);
        var neverFinishes = new TaskCompletionSource();
        _extractor.BeginExtractionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(neverFinishes.Task);

        var start = StartAsync();
        var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().BeSameAs(start);
        (await start).SubtitleUrl.Should().BeNull();
        neverFinishes.Task.IsCompleted.Should().BeFalse();
    }

    // Image subtitles are painted into the video by the encoder, so a sidecar
    // would double them up.
    [Fact]
    public async Task A_burn_in_subtitle_gets_no_sidecar()
    {
        Arrange(subtitleSelected: true, burnIn: true, cached: true);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await NoExtractionStarted();
    }

    [Fact]
    public async Task Subtitles_off_gets_no_sidecar()
    {
        Arrange(subtitleSelected: false, burnIn: false, cached: true);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await NoExtractionStarted();
    }

    // The cache is keyed on (part, track), so the answer doesn't depend on the
    // session — a hit is served without touching the part at all.
    [Fact]
    public async Task A_cache_hit_is_answered_from_the_session_alone()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: true);

        await StartAsync();

        _extractor.Received(1).TryGetCachedWebVtt(TempDir, PartId, SubtitleTrackId, out Arg.Any<string>());
        await _repo.DidNotReceive().GetMediaPartForSessionAsync(Arg.Any<Guid>());
    }

    // The fallback map is "the Nth subtitle of this file", and ffmpeg counts
    // those in stream order. The part's track collection comes back in whatever
    // order EF materialised it, so the ordinal has to be taken from the tracks
    // sorted by stream index — otherwise the retry lands on a different track
    // than the one the user picked, which is the exact failure it exists to fix.
    [Fact]
    public async Task The_ordinal_counts_subtitles_in_stream_order_not_collection_order()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);
        _repo.GetMediaPartForSessionAsync(SessionId).Returns(new MediaPart
        {
            Id = PartId,
            FilePath = "/media/movie.mkv",
            SubtitleTracks = new List<MediaSubtitleTrack>
            {
                new() { Id = SubtitleTrackId, StreamIndex = SubtitleStreamIndex, Codec = "subrip" },
                new() { Id = Guid.NewGuid(), StreamIndex = 2, Codec = "subrip" },
            }
        });

        await StartAsync();

        await _extractor.Received(1).BeginExtractionAsync(
            Arg.Any<string>(), SubtitleStreamIndex, SubtitleOrdinal, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task The_first_subtitle_of_a_file_is_ordinal_zero()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);
        _repo.GetMediaPartForSessionAsync(SessionId).Returns(new MediaPart
        {
            Id = PartId,
            FilePath = "/media/movie.mkv",
            SubtitleTracks = new List<MediaSubtitleTrack>
            {
                new() { Id = SubtitleTrackId, StreamIndex = SubtitleStreamIndex, Codec = "subrip" },
                new() { Id = Guid.NewGuid(), StreamIndex = 9, Codec = "subrip" },
            }
        });

        await StartAsync();

        await _extractor.Received(1).BeginExtractionAsync(
            Arg.Any<string>(), SubtitleStreamIndex, 0, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    // The HLS file route serves a file only when its name starts with the prefix
    // the token signs. The cache file is named for the part and track rather than
    // the transcode key, so the token has to sign that name — signing the media
    // item id would 404 every subtitle.
    [Fact]
    public async Task The_sidecar_url_satisfies_the_hls_routes_token_and_prefix_rules()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: true);

        var url = (await StartAsync()).SubtitleUrl!;

        var parts = url.Split('/');
        var fileName = parts[^1];
        var token = parts[^2];

        _signer.TryVerify(token, StreamManager.HlsTokenScope, out var prefix).Should().BeTrue();
        fileName.Should().Be(CachedName);
        fileName.Should().StartWith(prefix);
        url.Should().StartWith("/api/streaming/hls/s/");
    }

    // An extra has its own MediaPart, so it keys the cache on that rather than on
    // the parent item — two different files never share an extracted subtitle.
    [Fact]
    public async Task An_extras_sidecar_is_keyed_on_its_own_part()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false, extraId: ExtraId);

        await NewManager().StartExtraSessionAsync(ExtraId, "device-1", Guid.NewGuid(), Guid.NewGuid(), 0);

        await _extractor.Received(1).BeginExtractionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), TempDir, PartId, SubtitleTrackId);
    }

    // The session records a track the part no longer has (re-analysis dropped
    // it) — no sidecar, and no ffmpeg call with a bogus index.
    [Fact]
    public async Task An_unresolvable_subtitle_track_starts_no_extraction()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);
        _repo.GetMediaPartForSessionAsync(SessionId).Returns(new MediaPart { Id = PartId, FilePath = "/media/movie.mkv" });

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await NoExtractionStarted();
    }

    [Fact]
    public async Task The_cache_directory_falls_back_when_the_server_has_none_configured()
    {
        Arrange(subtitleSelected: true, burnIn: false, cached: false);
        _settings.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = "" });

        await StartAsync();

        await _extractor.Received(1).BeginExtractionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), StreamManager.DefaultTranscodeTempDirectory, PartId, SubtitleTrackId);
    }
}
