using NSubstitute;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;

namespace Vora.Application.Tests.Streaming;

public class PlaybackStatsTests
{
    private static readonly Guid AccountId = Guid.Parse("3c5d0d0f-6a76-4b8d-9a52-0d2a7b9f3f11");

    private readonly MediaVideoTrack _video = new() { StreamIndex = 0, Codec = "hevc", Profile = "Main 10", BitDepth = 10, HdrType = "HDR10", Bitrate = 14_000_000, Width = 3840, Height = 1600, FrameRate = 23.976 };
    private readonly MediaAudioTrack _audio = new() { StreamIndex = 1, Codec = "dts", Channels = 6, Language = "eng", Title = "DTS-HD MA 5.1", IsDefault = true };
    private readonly MediaSubtitleTrack _subtitle = new() { StreamIndex = 3, Codec = "hdmv_pgs_subtitle", Language = "eng", IsForced = true };

    private MediaPart Part() => new()
    {
        FilePath = "/media/movies/Dune (2021)/Dune.mkv",
        Container = "mkv",
        OverallBitrate = 18_500_000,
        VideoTracks = [_video],
        AudioTracks = [_audio],
        SubtitleTracks = [_subtitle],
    };

    private StreamSession Transcoding() => new()
    {
        UserId = AccountId,
        MediaItemId = Guid.NewGuid(),
        Strategy = "Transcode",
        VideoStrategy = "Transcode",
        AudioStrategy = "Transcode",
        SubtitleStrategy = "BurnIn",
        VideoCodec = "h264",
        AudioCodec = "aac",
        OutputWidth = 1920,
        OutputHeight = 800,
        OutputHdrType = "SDR",
        BandwidthKbps = 8000,
        TargetAudioChannels = 2,
        VideoTrackId = _video.Id,
        AudioTrackId = _audio.Id,
        SubtitleTrackId = _subtitle.Id,
    };

    private static readonly IReadOnlyList<MediaStreamFacts> Facts =
    [
        new(0, "video", "hevc", "Main 10", 153, null, null, null),
        new(1, "audio", "dts", "DTS-HD MA", null, 48000, "5.1(side)", 3_812_000),
    ];

    private static readonly TranscodeStatusDto Nvenc = new("h264_nvenc", "NVIDIA NVENC", "NVIDIA NVDEC", 6800, 8000, "hable (GPU, CUDA)", "aac", 2, 192, 96, 4.01, true, false, false);

    [Fact]
    public void A_video_bitrate_above_the_whole_files_is_not_shown()
    {
        _video.Bitrate = null;
        IReadOnlyList<MediaStreamFacts> staleTag = [new(0, "video", "hevc", "Main 10", 153, null, null, 23_986_068)];

        var stats = PlaybackStats.Build(Transcoding(), Part(), staleTag, Nvenc);

        stats.Video?.BitrateKbps.Should().BeNull();
        stats.SourceBitrateKbps.Should().Be(18_500);
    }

    [Fact]
    public void A_session_whose_video_track_is_gone_describes_the_video_not_the_cover_image()
    {
        var part = Part();
        part.VideoTracks = [new MediaVideoTrack { StreamIndex = 2, Codec = "mjpeg", Width = 600, Height = 900 }, _video];
        var session = Transcoding();
        session.VideoTrackId = Guid.NewGuid();

        var stats = PlaybackStats.Build(session, part, Facts, Nvenc);

        stats.Video?.Codec.Should().Be("hevc");
        stats.Video?.Width.Should().Be(3840);
    }

    [Fact]
    public void Describes_the_source_beside_what_the_viewer_gets()
    {
        var stats = PlaybackStats.Build(Transcoding(), Part(), Facts, Nvenc);

        stats.Method.Should().Be("Transcode");
        stats.Delivery.Should().Be("HLS");
        stats.SourceContainer.Should().Be("mkv");
        stats.SourceBitrateKbps.Should().Be(18500);
        stats.StreamBitrateKbps.Should().Be(8000);

        var video = Assert.IsType<PlaybackVideoStatsVM>(stats.Video);
        (video.Codec, video.Profile, video.Level, video.BitDepth, video.HdrType).Should().Be(("hevc", "Main 10", "5.1", 10, "HDR10"));
        (video.Width, video.Height, video.FrameRate, video.BitrateKbps).Should().Be((3840, 1600, 23.976, 14000));
        (video.OutputCodec, video.OutputWidth, video.OutputHeight, video.OutputHdrType).Should().Be(("h264", 1920, 800, "SDR"));
        (video.OutputBitrateKbps, video.OutputPeakKbps, video.ToneMapping).Should().Be((6800, 8000, "hable (GPU, CUDA)"));

        var audio = Assert.IsType<PlaybackAudioStatsVM>(stats.Audio);
        (audio.Codec, audio.Profile, audio.Channels, audio.ChannelLayout, audio.SampleRate, audio.BitrateKbps).Should().Be(("dts", "DTS-HD MA", 6, "5.1(side)", 48000, 3812));
        (audio.Strategy, audio.OutputCodec, audio.OutputChannels, audio.OutputBitrateKbps).Should().Be(("Transcode", "aac", 2, 192));

        var subtitle = Assert.IsType<PlaybackSubtitleStatsVM>(stats.Subtitle);
        (subtitle.Strategy, subtitle.IsForced).Should().Be(("BurnIn", true));
        var transcoder = Assert.IsType<PlaybackTranscoderStatsVM>(stats.Transcoder);
        (transcoder.EncodeHardware, transcoder.DecodeHardware, transcoder.Fps, transcoder.Speed).Should().Be(("NVIDIA NVENC", "NVIDIA NVDEC", 96.0, 4.01));
    }

    [Fact]
    public void Direct_play_is_the_file_itself_with_no_stream_bitrate_or_transcoder()
    {
        var session = Transcoding();
        session.Strategy = "DirectPlay";
        session.VideoStrategy = "DirectPlay";
        session.AudioStrategy = "DirectPlay";
        session.SubtitleTrackId = null;

        var stats = PlaybackStats.Build(session, Part(), Facts, transcode: null);

        stats.Delivery.Should().Be("File");
        stats.StreamBitrateKbps.Should().BeNull();
        stats.Transcoder.Should().BeNull();
        stats.Subtitle.Should().BeNull();
        Assert.IsType<PlaybackVideoStatsVM>(stats.Video).OutputBitrateKbps.Should().BeNull();
    }

    [Fact]
    public void Says_when_throttling_has_paused_the_transcode()
    {
        var stats = PlaybackStats.Build(Transcoding(), Part(), Facts, Nvenc with { Paused = true });

        Assert.IsType<PlaybackTranscoderStatsVM>(stats.Transcoder).Paused.Should().BeTrue();
    }

    [Theory]
    [InlineData("h264", 41, "4.1")]
    [InlineData("hevc", 153, "5.1")]
    [InlineData("hevc", 120, "4")]
    [InlineData("av1", 13, "5.1")]
    [InlineData("vp9", 51, "51")]
    public void Writes_the_codec_level_the_way_the_codec_numbers_it(string codec, int level, string expected)
    {
        PlaybackStats.Level(codec, level).Should().Be(expected);
    }

    [Fact]
    public async Task Only_the_account_that_started_a_stream_can_read_its_stats()
    {
        var repository = Substitute.For<IStreamRepository>();
        var session = Transcoding();
        repository.GetSessionAsync(session.Id).Returns(session);
        var manager = new PlaybackStatsManager(repository, Substitute.For<ITranscodeService>(), Substitute.For<IMediaAnalyzerService>(), new StreamFactsCache());

        (await manager.GetAsync(session.Id, Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();
        await repository.DidNotReceive().GetMediaPartForSessionAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Probes_a_file_once_however_often_the_panel_refreshes()
    {
        var repository = Substitute.For<IStreamRepository>();
        var analyzer = Substitute.For<IMediaAnalyzerService>();
        var transcoder = Substitute.For<ITranscodeService>();
        var session = Transcoding();
        var part = Part();
        repository.GetSessionAsync(session.Id).Returns(session);
        repository.GetMediaPartForSessionAsync(session.Id).Returns(part);
        analyzer.ReadStreamFactsAsync(part.FilePath, Arg.Any<CancellationToken>()).Returns(Facts);
        transcoder.GetStatus(session.MediaItemId).Returns(Nvenc);
        var manager = new PlaybackStatsManager(repository, transcoder, analyzer, new StreamFactsCache());

        await manager.GetAsync(session.Id, AccountId, TestContext.Current.CancellationToken);
        var stats = await manager.GetAsync(session.Id, AccountId, TestContext.Current.CancellationToken);

        Assert.IsType<PlaybackAudioStatsVM>(Assert.IsType<PlaybackStatsVM>(stats).Audio).Profile.Should().Be("DTS-HD MA");
        await analyzer.Received(1).ReadStreamFactsAsync(part.FilePath, Arg.Any<CancellationToken>());
    }
}
