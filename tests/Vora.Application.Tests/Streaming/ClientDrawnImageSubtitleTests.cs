using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class ClientDrawnImageSubtitleTests
{
    public enum Situation { DirectPlayPossible, BandwidthForced, HdrForced, ContainerForced, QualityPickForced }

    private sealed record Film(MediaStreamInfoDto Media, SubtitleStreamInfoDto Pgs, SubtitleStreamInfoDto Srt, SubtitleStreamInfoDto Sidecar);

    private static SubtitleStreamInfoDto Subtitle(string codec, int streamIndex, string language, bool external = false, bool forced = false) => new()
    {
        Id = Guid.NewGuid(), Codec = codec, StreamIndex = streamIndex, Language = language, IsExternal = external, IsForced = forced
    };

    private static TrackStreamInfoDto Picture(bool hdr) => new()
    {
        Id = Guid.NewGuid(), Codec = hdr ? "hevc" : "h264", IsDefault = true, StreamIndex = 0,
        Width = 1920, Height = 1080, FrameRate = 23.976, BitDepth = hdr ? 10 : 8, HdrType = hdr ? "HDR10" : null
    };

    private static MediaPartStreamInfoDto Version(Situation situation, string audioLanguage, string resolution = "1080p", long bitrate = 8_000_000, params SubtitleStreamInfoDto[] subtitles) => new()
    {
        Id = Guid.NewGuid(),
        Container = situation == Situation.ContainerForced ? "avi" : "mkv",
        Resolution = resolution,
        OverallBitrate = bitrate,
        VideoTracks = [Picture(hdr: situation == Situation.HdrForced)],
        AudioTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = "aac", Channels = 2, IsDefault = true, Language = audioLanguage, StreamIndex = 1 }],
        SubtitleTracks = [.. subtitles]
    };

    private static Film Arrival(Situation situation = Situation.DirectPlayPossible, string audioLanguage = "eng", string textLanguage = "eng", bool pgsForced = false)
    {
        var sidecar = Subtitle("subrip", 0, textLanguage, external: true);
        var pgs = Subtitle("hdmv_pgs_subtitle", 2, "eng", forced: pgsForced);
        var srt = Subtitle("subrip", 3, textLanguage);
        var media = new MediaStreamInfoDto { Id = Guid.NewGuid(), Parts = [Version(situation, audioLanguage, subtitles: [sidecar, pgs, srt])] };
        return new Film(media, pgs, srt, sidecar);
    }

    private static ClientDevice ShieldTv(bool? drawsImageSubtitles) => new()
    {
        DeviceType = "AndroidTV",
        MaxAudioChannels = 6,
        MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["h264", "hevc"],
        SupportedAudioCodecs = ["aac", "ac3", "eac3"],
        SupportedContainers = ["mkv", "mp4"],
        SupportedHdrFormats = [],
        RendersImageSubtitles = drawsImageSubtitles
    };

    private static Task<StreamDecisionDto> DecideAsync(MediaStreamInfoDto media, bool? draws, StreamingProfile profile, Situation situation = Situation.DirectPlayPossible,
        Guid? subtitleId = null, ServerSetting? server = null, ProfileTitlePlaybackChoice? remembered = null, int bandwidthKbps = 0)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        var setting = server ?? new ServerSetting();
        setting.StreamingProfile = profile;
        settings.GetSettingsAsync().Returns(setting);
        return new BestPathDecisionManager(settings).DetermineBestPathAsync(
            ShieldTv(draws), media,
            situation == Situation.BandwidthForced ? 2_000 : bandwidthKbps, "Client Limit",
            requestedSubtitleId: subtitleId,
            requestedMaxResolution: situation == Situation.QualityPickForced ? 720 : 0,
            audioOutput: new AudioOutputRequest(6, 0, PlaysAnyLayout: false, CanSwitchTracks: true),
            remembered: remembered);
    }

    public static TheoryData<StreamingProfile, Situation, bool?, bool> EveryCombination()
    {
        var data = new TheoryData<StreamingProfile, Situation, bool?, bool>();
        foreach (var profile in Enum.GetValues<StreamingProfile>())
        foreach (var situation in Enum.GetValues<Situation>())
        foreach (var draws in new bool?[] { null, false, true })
        foreach (var image in new[] { true, false })
            data.Add(profile, situation, draws, image);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public async Task A_client_draws_an_image_subtitle_only_from_the_original_file_and_everything_else_is_as_before(StreamingProfile profile, Situation situation, bool? draws, bool image)
    {
        var film = Arrival(situation);
        var picked = image ? film.Pgs : film.Srt;

        var decision = await DecideAsync(film.Media, draws, profile, situation, picked.Id);
        var today = await DecideAsync(film.Media, null, profile, situation, picked.Id);

        decision.SelectedSubtitleTrackId.Should().Be(picked.Id);
        if (image && decision.Strategy != StreamStrategy.DirectPlay) decision.RequiresSubtitleBurnIn.Should().BeTrue();

        if (image && draws == true && situation == Situation.DirectPlayPossible)
        {
            decision.Strategy.Should().Be(StreamStrategy.DirectPlay);
            decision.RequiresSubtitleBurnIn.Should().BeFalse();
            decision.SubtitleStrategy.Should().Be("DirectPlay");
            decision.SelectedSubtitleTrackPosition.Should().Be(0);
            decision.Reasons.Should().Contain("Subtitle: The client draws the image subtitle (hdmv_pgs_subtitle) itself, from the original file.");
            return;
        }

        decision.Should().BeEquivalentTo(today);
        if (image)
        {
            decision.RequiresSubtitleBurnIn.Should().BeTrue();
            decision.SubtitleStrategy.Should().Be("BurnIn");
            decision.VideoStrategy.Should().Be("Transcode");
            decision.SelectedSubtitleTrackPosition.Should().BeNull();
        }
        else
        {
            decision.RequiresSubtitleBurnIn.Should().BeFalse();
            decision.SubtitleStrategy.Should().Be("DirectPlay");
            decision.SelectedSubtitleTrackPosition.Should().Be(1);
            decision.Strategy.Should().Be(situation switch
            {
                Situation.DirectPlayPossible => StreamStrategy.DirectPlay,
                Situation.ContainerForced => StreamStrategy.Remux,
                _ => StreamStrategy.Transcode
            });
        }
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task A_forced_image_subtitle_picked_by_its_flag_is_left_to_the_client(StreamingProfile profile)
    {
        var film = Arrival(pgsForced: true);

        var drawn = await DecideAsync(film.Media, true, profile);
        var burned = await DecideAsync(film.Media, null, profile);

        drawn.SelectedSubtitleTrackId.Should().Be(film.Pgs.Id);
        (drawn.Strategy, drawn.RequiresSubtitleBurnIn, drawn.SelectedSubtitleTrackPosition).Should().Be((StreamStrategy.DirectPlay, false, 0));
        burned.SelectedSubtitleTrackId.Should().Be(film.Pgs.Id);
        (burned.Strategy, burned.RequiresSubtitleBurnIn, burned.SubtitleStrategy).Should().Be((StreamStrategy.Transcode, true, "BurnIn"));
    }

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task An_image_subtitle_turned_on_for_foreign_audio_is_left_to_the_client(StreamingProfile profile)
    {
        var film = Arrival(audioLanguage: "jpn", textLanguage: "fra");
        var server = new ServerSetting { AutoEnableSubtitlesForForeignAudio = true, MetadataLanguage = "eng" };

        var drawn = await DecideAsync(film.Media, true, profile, server: server);
        var burned = await DecideAsync(film.Media, null, profile, server: new ServerSetting { AutoEnableSubtitlesForForeignAudio = true, MetadataLanguage = "eng" });

        drawn.SelectedSubtitleTrackId.Should().Be(film.Pgs.Id);
        (drawn.Strategy, drawn.RequiresSubtitleBurnIn, drawn.SelectedSubtitleTrackPosition).Should().Be((StreamStrategy.DirectPlay, false, 0));
        burned.SelectedSubtitleTrackId.Should().Be(film.Pgs.Id);
        burned.RequiresSubtitleBurnIn.Should().BeTrue();
    }

    [Fact]
    public async Task A_sidecar_image_subtitle_is_still_burned_in_because_it_is_not_in_the_file_the_client_plays()
    {
        var vobSub = Subtitle("dvd_subtitle", 0, "eng", external: true);
        var media = new MediaStreamInfoDto { Id = Guid.NewGuid(), Parts = [Version(Situation.DirectPlayPossible, "eng", subtitles: [vobSub, Subtitle("hdmv_pgs_subtitle", 2, "eng")])] };

        var decision = await DecideAsync(media, true, StreamingProfile.DirectStreamPreference, subtitleId: vobSub.Id);

        decision.SelectedSubtitleTrackId.Should().Be(vobSub.Id);
        (decision.Strategy, decision.RequiresSubtitleBurnIn, decision.SelectedSubtitleTrackPosition).Should().Be((StreamStrategy.Transcode, true, (int?)null));
        decision.Should().BeEquivalentTo(await DecideAsync(media, null, StreamingProfile.DirectStreamPreference, subtitleId: vobSub.Id));
    }

    [Fact]
    public async Task A_sidecar_does_not_shift_the_position_of_an_embedded_subtitle()
    {
        var film = Arrival();

        var image = await DecideAsync(film.Media, true, StreamingProfile.DirectStreamPreference, subtitleId: film.Pgs.Id);
        var text = await DecideAsync(film.Media, true, StreamingProfile.DirectStreamPreference, subtitleId: film.Srt.Id);
        var sidecar = await DecideAsync(film.Media, true, StreamingProfile.DirectStreamPreference, subtitleId: film.Sidecar.Id);
        var none = await DecideAsync(new MediaStreamInfoDto { Id = Guid.NewGuid(), Parts = [Version(Situation.DirectPlayPossible, "eng")] }, true, StreamingProfile.DirectStreamPreference);

        image.SelectedSubtitleTrackPosition.Should().Be(0);
        text.SelectedSubtitleTrackPosition.Should().Be(1);
        sidecar.SelectedSubtitleTrackPosition.Should().BeNull();
        (none.SelectedSubtitleTrackId, none.SelectedSubtitleTrackPosition).Should().Be(((Guid?)null, (int?)null));
    }

    private static (MediaStreamInfoDto Media, MediaPartStreamInfoDto Uhd, MediaPartStreamInfoDto Hd, Guid PickedSubtitle) TwoVersions(string subtitleCodec)
    {
        var uhdSubtitle = Subtitle(subtitleCodec, 2, "eng");
        var uhd = Version(Situation.DirectPlayPossible, "eng", "2160p", 40_000_000, uhdSubtitle);
        uhd.VideoTracks[0].Width = 3840;
        uhd.VideoTracks[0].Height = 2160;
        uhd.VideoTracks[0].Codec = "hevc";
        var hd = Version(Situation.DirectPlayPossible, "eng", "1080p", 8_000_000, Subtitle(subtitleCodec, 2, "eng"));
        return (new MediaStreamInfoDto { Id = Guid.NewGuid(), Parts = [uhd, hd] }, uhd, hd, uhdSubtitle.Id);
    }

    private static ProfileTitlePlaybackChoice RememberedUhd() => new()
    {
        ProfileId = Guid.NewGuid(), DeviceId = "living-room", TitleId = Guid.NewGuid(),
        RemembersVersion = true, VersionHeight = 2160, VideoCodec = "hevc"
    };

    [Theory]
    [InlineData(StreamingProfile.DirectStreamPreference)]
    [InlineData(StreamingProfile.ClientPreference)]
    [InlineData(StreamingProfile.BandwidthOptimized)]
    public async Task A_remembered_version_over_the_limit_is_judged_as_if_the_drawn_subtitle_were_text(StreamingProfile profile)
    {
        var image = TwoVersions("hdmv_pgs_subtitle");
        var text = TwoVersions("subrip");

        var drawn = await DecideAsync(image.Media, true, profile, subtitleId: image.PickedSubtitle, remembered: RememberedUhd(), bandwidthKbps: 20_000);
        var today = await DecideAsync(image.Media, null, profile, subtitleId: image.PickedSubtitle, remembered: RememberedUhd(), bandwidthKbps: 20_000);
        var withText = await DecideAsync(text.Media, true, profile, subtitleId: text.PickedSubtitle, remembered: RememberedUhd(), bandwidthKbps: 20_000);

        if (profile == StreamingProfile.ClientPreference)
        {
            withText.SelectedMediaPartId.Should().Be(text.Uhd.Id);
            drawn.Should().BeEquivalentTo(today);
            (drawn.SelectedMediaPartId, drawn.RequiresSubtitleBurnIn, drawn.PickReplaced).Should().Be((image.Uhd.Id, true, false));
            return;
        }

        (withText.SelectedMediaPartId, withText.Strategy, withText.PickReplaced).Should().Be((text.Hd.Id, StreamStrategy.DirectPlay, true));
        (drawn.SelectedMediaPartId, drawn.Strategy, drawn.PickReplaced, drawn.RequiresSubtitleBurnIn).Should().Be((image.Hd.Id, StreamStrategy.DirectPlay, true, false));
        today.RequiresSubtitleBurnIn.Should().BeTrue();
    }
}
