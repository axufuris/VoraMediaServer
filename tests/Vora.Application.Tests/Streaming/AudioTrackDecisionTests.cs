using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class AudioTrackDecisionTests
{
    private static TrackStreamInfoDto Audio(int channels, string codec = "aac", bool isDefault = false, string language = "eng", string? title = null) => new()
    {
        Id = Guid.NewGuid(), Channels = channels, Codec = codec, IsDefault = isDefault, Language = language, Title = title
    };

    private static MediaStreamInfoDto Media(params TrackStreamInfoDto[] audio) => new()
    {
        Id = Guid.NewGuid(), Parts =
        [
            new MediaPartStreamInfoDto
            {
                Id = Guid.NewGuid(), Container = "mp4", Resolution = "1080p", OverallBitrate = 3_000_000,
                VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "h264", IsDefault = true }],
                AudioTracks = audio.ToList()
            }
        ]
    };

    private static async Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto media, int channels,
        StreamingProfile profile = StreamingProfile.DirectStreamPreference, Guid? requestedAudio = null,
        string[]? codecs = null, bool transcodeVideo = false)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { StreamingProfile = profile });
        return await new BestPathDecisionManager(settings).DetermineBestPathAsync(new ClientDevice
        {
            MaxAudioChannels = channels, DeviceType = "Browser",
            SupportedVideoCodecs = transcodeVideo ? [] : ["h264"],
            SupportedAudioCodecs = (codecs ?? ["aac", "ac3", "eac3"]).ToList(), SupportedContainers = ["mp4"]
        }, media, 0, "None", requestedAudioId: requestedAudio, requestedSubtitleId: Guid.Empty, requestedMaxResolution: 1080);
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task Stereo_output_uses_the_original_stereo_track_instead_of_downmixing_the_default_surround(StreamingProfile profile)
    {
        var stereo = Audio(2);
        var decision = await DecideAsync(Media(Audio(6, "eac3", true), stereo), 2, profile);

        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
        decision.AudioStrategy.Should().Be("DirectStream");
        decision.VideoStrategy.Should().Be("DirectStream");
        decision.Strategy.Should().Be(StreamStrategy.Remux);
        decision.TargetContainer.Should().Be("hls");
        decision.TargetAudioChannels.Should().Be(2);
        decision.Reason.Should().Contain("non-default track");
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    public async Task A_surround_output_prefers_six_channels_even_when_stereo_is_the_native_default(StreamingProfile profile)
    {
        var surround = Audio(6, "eac3");
        var decision = await DecideAsync(Media(Audio(2, isDefault: true), surround), 6, profile);

        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.AudioStrategy.Should().Be("DirectStream");
        decision.TargetAudioChannels.Should().Be(6);
        decision.Strategy.Should().Be(StreamStrategy.Remux);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task Uses_the_highest_original_channel_layout_that_fits_the_output(int channels)
    {
        var tracks = new[] { Audio(2), Audio(6), Audio(8, isDefault: true) };
        var decision = await DecideAsync(Media(tracks), channels);

        decision.SelectedAudioTrackId.Should().Be(tracks.Single(track => track.Channels == channels).Id);
        decision.AudioStrategy.Should().NotBe("Transcode");
        decision.TargetAudioChannels.Should().Be(channels);
    }

    [Fact]
    public async Task Compatible_default_surround_stays_direct_play()
    {
        var surround = Audio(6, "eac3", true);
        var decision = await DecideAsync(Media(surround, Audio(2)), 6);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
    }

    [Fact]
    public async Task Copies_stereo_audio_even_when_the_video_needs_transcoding()
    {
        var stereo = Audio(2);
        var decision = await DecideAsync(Media(Audio(6, "eac3", true), stereo), 6,
            profile: StreamingProfile.ClientPreference, codecs: ["aac"], transcodeVideo: true);

        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
        decision.VideoStrategy.Should().Be("Transcode");
        decision.AudioStrategy.Should().Be("DirectStream");
    }

    [Fact]
    public async Task A_manual_track_choice_is_preserved_even_when_conversion_is_required()
    {
        var surround = Audio(6, "eac3", true);
        var decision = await DecideAsync(Media(surround, Audio(2)), 2, requestedAudio: surround.Id);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.AudioStrategy.Should().Be("Transcode");
        decision.TargetAudioChannels.Should().Be(2);
        decision.Reason.Should().Contain("Downmix required");
    }

    [Fact]
    public async Task Chooses_a_better_supported_codec_for_the_same_channel_layout()
    {
        var surround = Audio(6, "eac3");
        var decision = await DecideAsync(Media(Audio(6, isDefault: true), surround), 6);
        decision.SelectedAudioTrackId.Should().Be(surround.Id);
        decision.AudioStrategy.Should().Be("DirectStream");
    }

    [Fact]
    public async Task Preserves_the_default_dialogue_language_when_choosing_a_channel_layout()
    {
        var englishStereo = Audio(2, language: "en");
        var decision = await DecideAsync(Media(Audio(6, "eac3", true), Audio(6, language: "fra"), englishStereo), 2);
        decision.SelectedAudioTrackId.Should().Be(englishStereo.Id);
    }

    [Fact]
    public async Task Does_not_automatically_play_commentary_to_avoid_an_audio_transcode()
    {
        var dialogue = Audio(6, "eac3", true);
        var decision = await DecideAsync(Media(dialogue, Audio(2, title: "Director commentary")), 2);
        decision.SelectedAudioTrackId.Should().Be(dialogue.Id);
    }

    [Fact]
    public async Task Hls_uses_a_copyable_track_instead_of_silently_converting_lossless_audio()
    {
        var stereo = Audio(2);
        var media = Media(Audio(6, "flac", true), stereo);
        media.Parts[0].Container = "mkv";
        var decision = await DecideAsync(media, 6, codecs: ["aac", "flac"]);
        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
        decision.AudioStrategy.Should().Be("DirectStream");
    }

    [Fact]
    public async Task Reports_hls_audio_conversion_when_there_is_no_copyable_track()
    {
        var media = Media(Audio(6, "flac", true));
        media.Parts[0].Container = "mkv";
        var decision = await DecideAsync(media, 6, codecs: ["flac"]);
        decision.AudioStrategy.Should().Be("Transcode");
        decision.TargetAudioChannels.Should().Be(6);
        decision.Reason.Should().Contain("HLS MPEG-TS");
    }

    [Fact]
    public async Task Native_lossless_audio_is_preserved_when_the_original_file_can_play()
    {
        var decision = await DecideAsync(Media(Audio(6, "flac", true), Audio(2)), 6, codecs: ["aac", "flac"]);
        decision.AudioStrategy.Should().Be("DirectPlay");
        decision.TargetAudioCodec.Should().Be("flac");
    }

    [Fact]
    public async Task Invalid_output_channel_counts_fall_back_to_stereo()
    {
        var stereo = Audio(2);
        var decision = await DecideAsync(Media(Audio(6, "eac3", true), stereo), 0);
        decision.SelectedAudioTrackId.Should().Be(stereo.Id);
        decision.TargetAudioChannels.Should().Be(2);
    }
}
