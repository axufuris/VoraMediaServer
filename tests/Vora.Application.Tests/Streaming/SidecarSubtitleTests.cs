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

// Text subtitles are stripped from the HLS output on purpose, so the only way a
// non-burn-in subtitle reaches the player is as a sidecar WebVTT alongside the
// session's segments. These pin who gets one, who doesn't, and that the URL the
// player is handed is one the HLS file route will actually serve.
public class SidecarSubtitleTests
{
    private const string TempDir = "/transcode";
    private const int SubtitleStreamIndex = 3;

    private static readonly Guid MediaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PartId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SubtitleTrackId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ExtraId = Guid.Parse("55555555-5555-5555-5555-555555555555");

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

    private void Arrange(bool subtitleSelected, bool burnIn, Guid? extraId = null)
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

        _extractor.ExtractWebVttAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => $"{call.ArgAt<Guid>(3)}_subtitles.vtt");
    }

    private Task<(StreamSession Session, string StreamUrl, string? SubtitleUrl)> StartAsync() =>
        NewManager().StartSessionAsync(MediaId, "device-1", Guid.NewGuid(), Guid.NewGuid(), 0);

    [Fact]
    public async Task A_text_subtitle_gets_a_sidecar_url()
    {
        Arrange(subtitleSelected: true, burnIn: false);

        var result = await StartAsync();

        result.SubtitleUrl.Should().NotBeNullOrEmpty();
    }

    // Image subtitles are painted into the video by the encoder, so a sidecar
    // would double them up.
    [Fact]
    public async Task A_burn_in_subtitle_gets_no_sidecar()
    {
        Arrange(subtitleSelected: true, burnIn: true);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await _extractor.DidNotReceive().ExtractWebVttAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subtitles_off_gets_no_sidecar()
    {
        Arrange(subtitleSelected: false, burnIn: false);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await _extractor.DidNotReceive().ExtractWebVttAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // A leftover .vtt from an earlier session on the same title is addressable
    // by anyone holding a prefix token, so a session that wants no subtitles has
    // to clear it rather than just decline to name it.
    [Fact]
    public async Task A_session_without_a_text_subtitle_clears_any_stale_sidecar()
    {
        Arrange(subtitleSelected: false, burnIn: false);

        await StartAsync();

        _extractor.Received(1).RemoveWebVtt(TempDir, MediaId);
    }

    // The subtitle stream index is absolute, the same form BuildFFmpegArguments
    // maps video and audio with. Reading it as a subtitle-relative index would
    // silently pick a different track whenever the subtitle isn't the Nth sub.
    [Fact]
    public async Task Extraction_is_asked_for_the_selected_tracks_absolute_stream_index()
    {
        Arrange(subtitleSelected: true, burnIn: false);

        await StartAsync();

        await _extractor.Received(1).ExtractWebVttAsync(
            "/media/movie.mkv", SubtitleStreamIndex, TempDir, MediaId, Arg.Any<CancellationToken>());
    }

    // The HLS file route serves anything whose name starts with the prefix its
    // token signs, so the sidecar has to be named and signed under the same
    // transcode key as the playlist and segments.
    [Fact]
    public async Task The_sidecar_url_satisfies_the_hls_routes_token_and_prefix_rules()
    {
        Arrange(subtitleSelected: true, burnIn: false);

        var url = (await StartAsync()).SubtitleUrl!;

        var parts = url.Split('/');
        parts.Should().HaveCountGreaterThan(2);
        var fileName = parts[^1];
        var token = parts[^2];

        _signer.TryVerify(token, StreamManager.HlsTokenScope, out var prefix).Should().BeTrue();
        prefix.Should().Be(MediaId.ToString());
        fileName.Should().StartWith(prefix);
        fileName.Should().EndWith(".vtt");
        url.Should().StartWith("/api/streaming/hls/s/");
    }

    // An extra transcodes under its own id so its stream can't collide with the
    // parent item's; the sidecar has to follow that key or its URL won't verify.
    [Fact]
    public async Task An_extras_sidecar_is_keyed_on_the_extra_not_the_parent_item()
    {
        Arrange(subtitleSelected: true, burnIn: false, extraId: ExtraId);

        var result = await NewManager().StartExtraSessionAsync(ExtraId, "device-1", Guid.NewGuid(), Guid.NewGuid(), 0);

        await _extractor.Received(1).ExtractWebVttAsync(
            Arg.Any<string>(), SubtitleStreamIndex, TempDir, ExtraId, Arg.Any<CancellationToken>());
        result.SubtitleUrl.Should().Contain($"{ExtraId}_subtitles.vtt");
    }

    // Extraction is best-effort: a source ffmpeg can't read must cost the
    // subtitle, not the stream.
    [Fact]
    public async Task A_failed_extraction_leaves_the_stream_playable_without_subtitles()
    {
        Arrange(subtitleSelected: true, burnIn: false);
        _extractor.ExtractWebVttAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        result.StreamUrl.Should().NotBeNullOrEmpty();
    }

    // The session records a track the part no longer has (re-analysis dropped
    // it) — no sidecar, and no ffmpeg call with a bogus index.
    [Fact]
    public async Task An_unresolvable_subtitle_track_produces_no_sidecar()
    {
        Arrange(subtitleSelected: true, burnIn: false);
        _repo.GetMediaPartForSessionAsync(SessionId).Returns(new MediaPart { Id = PartId, FilePath = "/media/movie.mkv" });

        var result = await StartAsync();

        result.SubtitleUrl.Should().BeNull();
        await _extractor.DidNotReceive().ExtractWebVttAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_temp_directory_falls_back_when_the_server_has_none_configured()
    {
        Arrange(subtitleSelected: true, burnIn: false);
        _settings.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = "" });

        await StartAsync();

        await _extractor.Received(1).ExtractWebVttAsync(
            Arg.Any<string>(), Arg.Any<int>(), StreamManager.DefaultTranscodeTempDirectory, MediaId, Arg.Any<CancellationToken>());
    }
}
