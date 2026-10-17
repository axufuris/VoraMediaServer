using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming.Matrix;

public static class MatrixRunner
{
    private static readonly Guid TextSubtitle = Guid.NewGuid();
    private static readonly Guid ImageSubtitle = Guid.NewGuid();

    public static async Task<MatrixOutcome> RunAsync(MatrixScenario scenario)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting
        {
            StreamingProfile = scenario.Profile,
            HdrTranscodeDownscale = scenario.Situation.HdrDownscaleAlways ? "Always" : "Auto"
        });

        var versions = new Dictionary<Guid, MatrixVersion>();
        var tracks = new Dictionary<Guid, MatrixTrack>();
        var media = new MediaStreamInfoDto { Id = Guid.NewGuid(), Parts = [] };
        foreach (var version in scenario.Title.Versions)
        {
            var part = PartFor(version, scenario.Situation);
            versions[part.Id] = version;
            foreach (var track in version.Tracks)
            {
                var audio = new TrackStreamInfoDto
                {
                    Id = Guid.NewGuid(), Codec = track.Codec, Channels = track.Channels, IsDefault = track.IsDefault, Language = track.Language,
                    Title = track.Commentary ? "Director commentary" : track.Label, StreamIndex = part.AudioTracks.Count + 1
                };
                tracks[audio.Id] = track;
                part.AudioTracks.Add(audio);
            }
            media.Parts.Add(part);
        }

        var client = scenario.Client;
        var device = new ClientDevice
        {
            DeviceType = client.DeviceType,
            SupportedVideoCodecs = client.Video.ToList(), SupportedAudioCodecs = client.Audio.ToList(), SupportedContainers = client.Containers.ToList(),
            SupportedHdrFormats = client.Hdr?.ToList(), MaxVideoBitDepth = client.BitDepth,
            MaxAudioChannels = client.OlderSingleValue(scenario.ChannelPick)
        };
        var audioOutput = client.OldApp ? null : new AudioOutputRequest(client.Speakers, scenario.ChannelPick, client.IsBrowser, client.SwitchesTracks);
        Guid? manualTrack = scenario.ManualTrack == null ? null : tracks.First(pair => pair.Value.Label == scenario.ManualTrack).Key;
        Guid? manualVersion = scenario.ManualVersion == null ? null : versions.First(pair => pair.Value.Label == scenario.ManualVersion).Key;
        Guid? subtitle = scenario.Situation.Subtitle switch
        {
            MatrixSituation.TextSubtitle => TextSubtitle,
            MatrixSituation.ImageSubtitle => ImageSubtitle,
            MatrixSituation.ForcedImageSubtitle => null,
            _ => Guid.Empty
        };

        var decision = await new BestPathDecisionManager(settings).DetermineBestPathAsync(device, media, scenario.Situation.LimitKbps, scenario.Situation.LimitSource,
            requestedAudioId: manualTrack, requestedSubtitleId: subtitle, requestedMaxResolution: scenario.MaxHeight, requestedMediaPartId: manualVersion, audioOutput: audioOutput, hlsVideoCodecs: client.HlsVideo,
            preferences: scenario.PreferredLanguage == null ? null : new TrackPreferences(scenario.PreferredLanguage, null, null, false),
            display: client.Display > 0 ? new DisplayHint((int)Math.Round(client.Display * 16 / 9.0), client.Display) : null,
            gpuEncodesHdr: scenario.Situation.GpuKeepsHdr);

        var chosenVersion = versions[decision.SelectedMediaPartId];
        var selected = tracks[decision.SelectedAudioTrackId];
        bool plainPlayerDecides = decision.Strategy == StreamStrategy.DirectPlay && !client.SwitchesTracks;
        var heard = plainPlayerDecides ? chosenVersion.PlainPlayerStartsOn(client) : selected;
        bool converted = decision.AudioStrategy == "Transcode";
        int heardChannels = converted ? decision.TargetAudioChannels : heard.Channels;
        string heardCodec = converted ? decision.TargetAudioCodec : heard.Codec;
        int outputHeight = string.IsNullOrEmpty(decision.OutputResolution) ? chosenVersion.Height : BestPathDecisionManager.ParseHeightFromResolution(decision.OutputResolution);

        return new MatrixOutcome(decision.Strategy, decision.VideoStrategy, decision.AudioStrategy, chosenVersion, selected, heard, heardChannels, heardCodec, outputHeight,
            decision.Reason, MarginOverADifferentKindOfOutcome(decision), decision.BandwidthKbps, decision.RequiresSubtitleBurnIn,
            string.IsNullOrEmpty(decision.OutputHdrType) ? "SDR" : decision.OutputHdrType);
    }

    private static MediaPartStreamInfoDto PartFor(MatrixVersion version, MatrixSituation situation) => new()
    {
        Id = Guid.NewGuid(), Container = version.Container, Resolution = version.Height >= 2160 ? "2160p" : $"{version.Height}p", OverallBitrate = version.Kbps * 1000L,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), Codec = version.VideoCodec, IsDefault = true, HdrType = version.Hdr, BitDepth = version.BitDepth }],
        AudioTracks = [],
        SubtitleTracks =
        [
            new SubtitleStreamInfoDto { Id = TextSubtitle, Codec = "subrip", Language = "eng" },
            new SubtitleStreamInfoDto { Id = ImageSubtitle, Codec = "hdmv_pgs_subtitle", Language = "eng", IsForced = situation.Subtitle == MatrixSituation.ForcedImageSubtitle }
        ]
    };

    private static int MarginOverADifferentKindOfOutcome(StreamDecisionDto decision)
    {
        var ranked = decision.EvaluatedOptions.Where(option => option.Considered).OrderBy(option => option.PenaltyScore).ToList();
        var rival = ranked.Skip(1).FirstOrDefault(option =>
            option.Strategy != ranked[0].Strategy || option.AudioStrategy != ranked[0].AudioStrategy || option.VideoStrategy != ranked[0].VideoStrategy);
        return rival == null ? int.MaxValue : rival.PenaltyScore - ranked[0].PenaltyScore;
    }
}
