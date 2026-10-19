using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Plugins;

namespace Vora.Application.Streaming;

public interface IBestPathDecisionManager
{
    Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0, Guid? requestedMediaPartId = null, AudioOutputRequest? audioOutput = null, IReadOnlyCollection<string>? hlsVideoCodecs = null, ProfileTitlePlaybackChoice? remembered = null, TrackPreferences? preferences = null, int requestedMaxWidth = 0, DisplayHint? display = null, bool gpuEncodesHdr = false);
}

public class BestPathDecisionManager : IBestPathDecisionManager
{
    private readonly ISystemSettingsRepository _settingsRepo;

    public BestPathDecisionManager(ISystemSettingsRepository settingsRepo)
    {
        _settingsRepo = settingsRepo;
    }

    private const int ConvertedVideoPenaltyWhenAnotherVersionMayPlayUntouched = 25000;

    private class StreamOption
    {
        public StreamDecisionDto Decision { get; set; } = new();
        public int PenaltyScore { get; set; }
        public bool Exact { get; set; }
        public bool Family { get; set; }
        public bool VideoUntouched { get; set; }
        public bool AudioUntouched { get; set; }
        public StreamOptionLogDto? Log { get; set; }
    }

    public async Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0, Guid? requestedMediaPartId = null, AudioOutputRequest? audioOutput = null, IReadOnlyCollection<string>? hlsVideoCodecs = null, ProfileTitlePlaybackChoice? remembered = null, TrackPreferences? preferences = null, int requestedMaxWidth = 0, DisplayHint? display = null, bool gpuEncodesHdr = false)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        if (client.RendersImageSubtitles == true)
        {
            var drawnByClient = Decide(settings, clientDrawsImageSubtitles: true, client, mediaItem, maxAllowedBandwidthKbps, bandwidthLimitSource, requestedVideoId, requestedAudioId, requestedSubtitleId, requestedMaxResolution, requestedMediaPartId, audioOutput, hlsVideoCodecs, remembered, preferences, requestedMaxWidth, display, gpuEncodesHdr);
            if (!drawnByClient.LeftAnImageSubtitleToTheClient || drawnByClient.Decision.Strategy == StreamStrategy.DirectPlay) return drawnByClient.Decision;
        }
        return Decide(settings, clientDrawsImageSubtitles: false, client, mediaItem, maxAllowedBandwidthKbps, bandwidthLimitSource, requestedVideoId, requestedAudioId, requestedSubtitleId, requestedMaxResolution, requestedMediaPartId, audioOutput, hlsVideoCodecs, remembered, preferences, requestedMaxWidth, display, gpuEncodesHdr).Decision;
    }

    private static (StreamDecisionDto Decision, bool LeftAnImageSubtitleToTheClient) Decide(ServerSetting settings, bool clientDrawsImageSubtitles, ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId, Guid? requestedAudioId, Guid? requestedSubtitleId, int requestedMaxResolution, Guid? requestedMediaPartId, AudioOutputRequest? audioOutput, IReadOnlyCollection<string>? hlsVideoCodecs, ProfileTitlePlaybackChoice? remembered, TrackPreferences? preferences, int requestedMaxWidth, DisplayHint? display, bool gpuEncodesHdr)
    {
        var trackPreferences = preferences ?? TrackPreferences.Automatic;
        var subtitlePreference = trackPreferences.SubtitlesOn(settings);
        var audioLimits = AudioOutputLimits.Resolve(client.MaxAudioChannels, audioOutput, settings.StreamingProfile);
        bool clientSwitchesTracks = audioOutput is { CanSwitchTracks: true };
        var hlsVideo = HlsVideoCarriage.For(client.DeviceType, hlsVideoCodecs);
        bool clientPlaysDolbyDigital = client.DeviceType != "Browser" && client.SupportedAudioCodecs.Contains(AudioPlayback.DolbyDigital);
        bool prefersUntouchedVideo = settings.StreamingProfile != StreamingProfile.ClientPreference;
        var options = new List<StreamOption>();
        var decisionLogs = new List<StreamOptionLogDto>();
        bool leftAnImageSubtitleToTheClient = false;

        var wish = PlaybackWish.For(mediaItem, requestedMediaPartId, requestedVideoId, requestedAudioId, requestedSubtitleId, remembered);

        foreach (var part in mediaItem.Parts)
        {
            var videoTracksToEvaluate = CoverImageTracks.Without(part.VideoTracks, track => track.Codec);
            var videoTrack = part.VideoTracks.FirstOrDefault(t => wish.Version?.VideoTrackId == t.Id)
                ?? videoTracksToEvaluate.FirstOrDefault(t => t.IsDefault)
                ?? videoTracksToEvaluate.FirstOrDefault();
            if (videoTrack == null) continue;

            var videoCodec = videoTrack.Codec?.ToLower() ?? "";
            var container = part.Container?.ToLower() ?? "";

            var sourceSize = OutputSize.Source(videoTrack.Width, videoTrack.Height, part.Resolution);
            var clientBox = OutputSize.ClientBox(requestedMaxWidth, requestedMaxResolution);
            var screenBox = OutputSize.ScreenBox(clientBox, display);
            int trackHeight = sourceSize is { } knownSource ? OutputSize.HeightClass(knownSource) : 0;
            bool is4k = trackHeight >= 2160;
            int clientMaxRes = clientBox.Height;
            var playbackBox = OutputSize.PlaybackBox(requestedMaxWidth, requestedMaxResolution);
            bool exceedsClientSize = sourceSize is { } measured && !OutputSize.FitsPlayback(measured, playbackBox);

            // What the client can decode and display, versus what the encoder
            // can actually hand it. A 1344p panel is served 1080p, so scoring
            // has to compare against 1080 — crediting an option with delivering
            // 1344p made a 4K transcode look like a perfect fit while a 1080p
            // part that direct-plays was penalised for missing a height no file
            // and no encoder can produce. clientMaxRes stays raw for the
            // capability test below: that one is about the device's limit, and
            // snapping it would let a 1440p source through to this panel.
            int deliverableMaxRes = SnapToDeliverableHeight(screenBox.Height);
            int ceilingDeliverableRes = SnapToDeliverableHeight(clientMaxRes);

            // Effective output height for THIS part once any auto-downscale
            // rules fire. Used by the resolution-fit penalty below so the
            // scorer can compare "what does the user actually see if we
            // pick this part?" across all candidates. We compute it now
            // because the HDR auto-downscale check needs the video
            // track's HdrType + the admin setting.
            bool isHdrPart = !string.IsNullOrWhiteSpace(videoTrack.HdrType)
                && !string.Equals(videoTrack.HdrType, "SDR", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(videoTrack.HdrType, "None", StringComparison.OrdinalIgnoreCase);
            var audioInFileOrder = part.AudioTracks.OrderBy(t => t.StreamIndex ?? int.MaxValue).ToList();
            var primaryAudioTrack = audioInFileOrder.FirstOrDefault(t => t.IsDefault) ?? audioInFileOrder.FirstOrDefault();
            bool everyPlayerStartsOnPrimary = primaryAudioTrack?.Id == audioInFileOrder.FirstOrDefault()?.Id;

            var wishedAudio = wish.Audio == null ? [] : part.AudioTracks.Where(wish.AudioFamily).ToList();
            var audioTracksToEvaluate = wishedAudio.Count > 0 ? wishedAudio : AutomaticAudioTracks(part.AudioTracks, trackPreferences.AudioLanguage, primaryAudioTrack);
            var heardLanguage = (audioTracksToEvaluate.FirstOrDefault(track => track.Id == primaryAudioTrack?.Id) ?? audioTracksToEvaluate.FirstOrDefault())?.Language;

            var subTrack = wish.SubtitleFor(part, () => PickAutomaticSubtitle(part.SubtitleTracks, heardLanguage, subtitlePreference));

            var subCodec = subTrack?.Codec?.ToLower() ?? "";
            bool imageSubtitle = subTrack != null && IsImageSubtitleCodec(subCodec);
            bool clientDrawsSubtitle = imageSubtitle && clientDrawsImageSubtitles && subTrack is { IsExternal: false };
            bool requiresBurnIn = imageSubtitle && !clientDrawsSubtitle;
            leftAnImageSubtitleToTheClient |= clientDrawsSubtitle;
            int? subtitlePosition = subTrack is { IsExternal: false } && !requiresBurnIn ? EmbeddedPosition(part.SubtitleTracks, subTrack) : null;

            int bitrateKbps = (int)((part.OverallBitrate ?? 3000000) / 1000);
            int sourceVideoKbps = videoTrack.Bitrate is > 0 ? (int)(videoTrack.Bitrate.Value / 1000) : bitrateKbps;
            bool exceedsBandwidth = maxAllowedBandwidthKbps > 0 && bitrateKbps > maxAllowedBandwidthKbps;

            var renderable = RenderableHdrFormats(videoTrack.HdrType);
            bool hdrNotRenderable = false;
            if (renderable.Count > 0 && client.SupportedHdrFormats != null)
            {
                var clientHdr = client.SupportedHdrFormats
                    .Select(NormalizeHdr)
                    .Where(h => h != null)
                    .Select(h => h!)
                    .ToHashSet();
                // Renderable if the client supports the source's format OR any
                // cross-compatible base layer it carries (Profile 8 DoVi, HDR10+).
                hdrNotRenderable = !renderable.Overlaps(clientHdr);
            }

            bool bitDepthTooHigh = client.MaxVideoBitDepth > 0 && (videoTrack.BitDepth ?? 8) > client.MaxVideoBitDepth;

            var hdrKeepsOff = isHdrPart && !hdrNotRenderable ? WhyHdrIsNotKept(client, hlsVideo, renderable, requiresBurnIn, gpuEncodesHdr, settings, videoCodec) : null;
            var keptHdr = isHdrPart && !hdrNotRenderable && hdrKeepsOff == null ? HdrKeptInConversion(renderable, client) : null;
            var outputCodec = keptHdr != null ? VideoCodec.Hevc : VideoCodec.H264;

            int effectiveTargetHeight = deliverableMaxRes;
            var outputBox = screenBox;
            if (isHdrPart && keptHdr == null && string.Equals(settings.HdrTranscodeDownscale, "Always", StringComparison.OrdinalIgnoreCase))
            {
                effectiveTargetHeight = Math.Min(effectiveTargetHeight, 1080);
                outputBox = OutputSize.Within(outputBox, OutputSize.HdrDownscaleBox);
            }
            if (maxAllowedBandwidthKbps > 0)
            {
                outputBox = outputBox with { Width = Math.Min(outputBox.Width, OutputSize.MaxWidthForBitrate(maxAllowedBandwidthKbps, videoTrack.FrameRate, outputCodec)) };
            }
            FrameSize? outputSize = sourceSize is { } fitted ? OutputSize.Fit(fitted, outputBox) : null;
            int outputHeight = outputSize is { } output ? OutputSize.HeightClass(output) : Math.Min(trackHeight, effectiveTargetHeight);

            foreach (var audioTrack in audioTracksToEvaluate)
            {
                var audioCodec = audioTrack.Codec?.ToLower() ?? "";
                int trackChannels = audioTrack.Channels is > 0 ? audioTrack.Channels.Value : 2;
                bool isPrimaryAudio = audioTrack.Id == primaryAudioTrack?.Id;

                bool codecUnsupported = !string.IsNullOrEmpty(videoCodec) && !client.SupportedVideoCodecs.Contains(videoCodec);
                bool needsVideoTranscode = codecUnsupported || requiresBurnIn || exceedsBandwidth || hdrNotRenderable || bitDepthTooHigh;
                bool needsAudioDownmix = audioLimits.NeedsDownmix(trackChannels);
                bool audioCodecUnsupported = !string.IsNullOrEmpty(audioCodec) && !client.SupportedAudioCodecs.Contains(audioCodec);
                bool needsAudioTranscode = audioCodecUnsupported || needsAudioDownmix;
                bool containerUnsupported = !string.IsNullOrEmpty(container) && !ContainerFamily.PlaysIn(client.SupportedContainers, container);
                bool serverDeliversTrack = !clientSwitchesTracks && !(isPrimaryAudio && everyPlayerStartsOnPrimary);
                bool needsRemux = containerUnsupported || serverDeliversTrack;

                var reasons = new List<string>();

                if (exceedsClientSize)
                {
                    needsVideoTranscode = true;
                    reasons.Add($"Video Transcode: Track resolution ({FrameText(sourceSize)}) exceeds what the client plays ({playbackBox.Width}x{playbackBox.Height}).");
                }

                bool audioUnsupportedInHls = !needsAudioTranscode
                    && (needsVideoTranscode || needsRemux)
                    && !AudioPlayback.CanCopyToHls(audioCodec);
                needsAudioTranscode |= audioUnsupportedInHls;

                bool videoReencodedForHls = !needsVideoTranscode
                    && (needsAudioTranscode || needsRemux)
                    && !string.IsNullOrEmpty(videoCodec)
                    && !hlsVideo.Contains(videoCodec);
                needsVideoTranscode |= videoReencodedForHls;
                bool videoConvertsForItself = codecUnsupported || exceedsBandwidth || hdrNotRenderable || bitDepthTooHigh || exceedsClientSize || videoReencodedForHls;

                var conversion = audioLimits.ConversionOf(trackChannels, clientPlaysDolbyDigital);
                int heardChannels = needsAudioTranscode ? conversion.Channels : trackChannels;
                string heardCodec = needsAudioTranscode ? conversion.Codec : audioCodec;

                var option = new StreamOption
                {
                    Exact = wish.VersionExact(part, videoTrack) && wish.AudioExact(audioTrack),
                    Family = wish.VersionFamily(part) && wish.AudioFamily(audioTrack),
                    VideoUntouched = !videoConvertsForItself,
                    AudioUntouched = !needsAudioTranscode && !videoReencodedForHls,
                    Decision = new StreamDecisionDto
                    {
                        SelectedMediaPartId = part.Id,
                        SelectedVideoTrackId = videoTrack.Id,
                        SelectedAudioTrackId = audioTrack.Id,
                        SelectedAudioTrackPosition = audioInFileOrder.IndexOf(audioTrack),
                        SelectedSubtitleTrackId = subTrack?.Id,
                        SelectedSubtitleTrackPosition = subtitlePosition,
                        RequiresSubtitleBurnIn = requiresBurnIn,
                        TargetAudioChannels = heardChannels,
                        SubtitleStrategy = subTrack == null ? "None" : (requiresBurnIn ? "BurnIn" : "DirectPlay")
                    }
                };

                int currentScore = 0;

                if (needsVideoTranscode && settings.DisableVideoTranscoding)
                {
                    currentScore += 100000;
                    reasons.Add("Video Transcode: Disabled by server administrator.");
                    needsVideoTranscode = false;
                }

                if (needsVideoTranscode || needsAudioTranscode)
                {
                    option.Decision.Strategy = StreamStrategy.Transcode;
                    option.Decision.TargetContainer = "hls";

                    currentScore += settings.StreamingProfile == StreamingProfile.DirectStreamPreference ? 50000 : 100;

                    if (needsVideoTranscode)
                    {
                        option.Decision.VideoStrategy = "Transcode";
                        option.Decision.TargetVideoCodec = keptHdr != null ? "hevc" : "h264";

                        if (requiresBurnIn) reasons.Add($"Video Transcode: Subtitle burn-in required ({subCodec}).");
                        else if (exceedsBandwidth) reasons.Add($"Video Transcode: Bitrate ({(bitrateKbps / 1000.0):F1} Mbps) exceeds {bandwidthLimitSource} ({(maxAllowedBandwidthKbps / 1000.0):F1} Mbps).");
                        else if (videoReencodedForHls) reasons.Add($"Video Transcode: This client's HLS player cannot take '{videoCodec}', and the stream has to be HLS.");
                        else if (exceedsClientSize) { }
                        else if (codecUnsupported) reasons.Add($"Video Transcode: '{videoCodec}' unsupported.");

                        if (hdrNotRenderable) reasons.Add($"Video Transcode: Source HDR ({videoTrack.HdrType}) not supported by client; tonemapping to SDR.");
                        else if (keptHdr != null) reasons.Add($"Video Transcode: Keeping {keptHdr} by encoding 10-bit HEVC.");
                        else if (hdrKeepsOff != null) reasons.Add($"Video Transcode: {videoTrack.HdrType} is tone-mapped to SDR because {hdrKeepsOff}.");
                        if (bitDepthTooHigh) reasons.Add($"Video Transcode: Source bit depth ({videoTrack.BitDepth}-bit) exceeds client maximum ({client.MaxVideoBitDepth}-bit).");

                        currentScore += is4k ? 200 : 50;
                        if (prefersUntouchedVideo) currentScore += ConvertedVideoPenaltyWhenAnotherVersionMayPlayUntouched;
                    }
                    else
                    {
                        option.Decision.VideoStrategy = "DirectStream";
                        option.Decision.TargetVideoCodec = videoCodec;
                        reasons.Add("Video Direct Stream: Supported.");
                    }

                    if (needsAudioTranscode)
                    {
                        option.Decision.AudioStrategy = "Transcode";
                        option.Decision.TargetAudioCodec = conversion.Codec;

                        if (needsAudioDownmix) reasons.Add($"Audio Transcode: Downmix required ({trackChannels}ch > {audioLimits.Hard}ch).");
                        else if (audioCodecUnsupported) reasons.Add($"Audio Transcode: '{audioCodec}' unsupported.");
                        else reasons.Add($"Audio Transcode: '{audioCodec}' cannot be copied to HLS MPEG-TS.");

                        currentScore += AudioPlayback.ConversionPenalty;
                    }
                    else
                    {
                        option.Decision.AudioStrategy = "DirectStream";
                        option.Decision.TargetAudioCodec = audioCodec;
                        reasons.Add("Audio Direct Stream: Supported.");
                    }
                }
                else if (needsRemux)
                {
                    option.Decision.Strategy = StreamStrategy.Remux;
                    option.Decision.VideoStrategy = "DirectStream";
                    option.Decision.AudioStrategy = "DirectStream";
                    option.Decision.TargetVideoCodec = videoCodec;
                    option.Decision.TargetAudioCodec = audioCodec;
                    option.Decision.TargetContainer = "hls";
                    if (containerUnsupported) reasons.Add($"Remux: Container '{container}' unsupported. Copying streams to HLS.");
                    if (serverDeliversTrack) reasons.Add("Audio Direct Stream: Copying the selected track to HLS because this client cannot switch tracks.");
                    currentScore += AudioPlayback.RemuxPenalty;
                }
                else
                {
                    option.Decision.Strategy = StreamStrategy.DirectPlay;
                    option.Decision.VideoStrategy = "DirectPlay";
                    option.Decision.AudioStrategy = "DirectPlay";
                    option.Decision.TargetVideoCodec = videoCodec;
                    option.Decision.TargetAudioCodec = audioCodec;
                    option.Decision.TargetContainer = container;
                    reasons.Add("Direct Play: Native support for all streams.");
                    if (!(isPrimaryAudio && everyPlayerStartsOnPrimary)) reasons.Add("Audio: The client switches to the selected track.");
                    if (clientDrawsSubtitle) reasons.Add($"Subtitle: The client draws the image subtitle ({subCodec}) itself, from the original file.");
                }

                // Resolution-fit penalty. Two components added to the
                // score so the lowest-penalty option wins:
                //
                //   qualityLossPenalty (heavy, weight 10):
                //     punishes parts whose output is below what the
                //     user/client asked for. deliverableMaxRes is the
                //     ceiling snapped to a rung the encoder can emit
                //     (RequestedMaxResolution or 2160 default);
                //     outputHeight is what this part can actually
                //     deliver under the current HDR-downscale rules.
                //     Both sides must be ladder heights: scored against
                //     a raw 1344 no option can ever reach zero, and a
                //     4K downscale to 1344 scored as a perfect fit
                //     while the 1080p part that direct-plays did not.
                //     Heavy weight ensures a 4K source downscaled to
                //     1080p still wins over a 720p source when the
                //     target is 1080p — the user gets the quality they
                //     asked for, not a smaller file.
                //
                //   downscaleWorkPenalty (light, weight 1):
                //     small tiebreaker that prefers the closest-fit
                //     part when multiple parts can hit the same
                //     output. With a 4K HDR + 1080p SDR pair targeted
                //     at 1080p, both produce 1080p output — but the
                //     1080p SDR part needs zero downscale work, so
                //     it wins on the work penalty. Same idea for
                //     1440p + 4K when target is 1080p: 1440p wins
                //     because it's closer to the target source-side.
                int deliveredHeight = needsVideoTranscode ? outputHeight : trackHeight;
                int qualityLossPenalty = Math.Max(0, deliverableMaxRes - deliveredHeight) * 10;
                int beyondScreenPenalty = Math.Max(0, ceilingDeliverableRes - Math.Max(deliveredHeight, deliverableMaxRes));
                int downscaleWorkPenalty = Math.Max(0, trackHeight - deliveredHeight);
                currentScore += qualityLossPenalty + beyondScreenPenalty + downscaleWorkPenalty;

                if (audioTrack.IsDefault) currentScore -= 2;

                currentScore += audioLimits.FitPenalty(trackChannels, heardChannels, heardCodec, needsAudioTranscode);
                if (videoReencodedForHls) currentScore += HlsVideoCarriage.ReencodePenalty;

                if (PlaybackWish.IsCommentary(audioTrack))
                {
                    currentScore += 500;
                    reasons.Add("[Commentary Track Penalized]");
                }

                int finalBandwidthKbps = exceedsBandwidth ? maxAllowedBandwidthKbps : bitrateKbps;
                int profileCapKbps = settings.StreamingProfile == StreamingProfile.BandwidthOptimized ? LadderKbpsForHeight(outputHeight) : 0;
                int averageKbps;
                if (needsVideoTranscode)
                {
                    var rate = VideoBitrate.For(sourceVideoKbps, videoCodec, videoTrack.BitDepth, sourceSize, outputSize, outputCodec, maxAllowedBandwidthKbps, profileCapKbps);
                    finalBandwidthKbps = rate.MaxKbps;
                    averageKbps = rate.TargetKbps;
                }
                else
                {
                    if (profileCapKbps > 0 && needsAudioTranscode) finalBandwidthKbps = Math.Min(finalBandwidthKbps, profileCapKbps);
                    averageKbps = finalBandwidthKbps;
                }
                double finalMbps = averageKbps / 1000.0;

                option.Decision.Reason = string.Join(" ", reasons);
                option.Decision.Reasons = [.. reasons];
                option.Decision.BandwidthKbps = finalBandwidthKbps;
                option.Decision.Quality = (needsVideoTranscode || needsAudioTranscode) ? $"Transcode ({finalMbps:F1} Mbps)" : $"Original ({finalMbps:F1} Mbps)";

                // Resolve the actual delivered resolution + HDR type for
                // this option. For Transcode the encoder is going to emit
                // outputHeight pixels of SDR (we always tonemap HDR sources
                // during transcode). For Remux and DirectPlay the source's
                // resolution and HDR characteristics pass straight through.
                if (needsVideoTranscode)
                {
                    option.Decision.OutputResolution = FormatHeightAsResolution(outputHeight);
                    option.Decision.OutputHdrType = keptHdr ?? "SDR";
                    if (outputSize is { } size)
                    {
                        option.Decision.OutputWidth = size.Width;
                        option.Decision.OutputHeight = size.Height;
                    }
                }
                else
                {
                    option.Decision.OutputResolution = part.Resolution ?? string.Empty;
                    option.Decision.OutputHdrType = string.IsNullOrWhiteSpace(videoTrack.HdrType) ? "SDR" : videoTrack.HdrType;
                }

                option.PenaltyScore = currentScore;

                options.Add(option);

                var log = new StreamOptionLogDto
                {
                    MediaPartId = part.Id,
                    PenaltyScore = option.PenaltyScore,
                    Strategy = option.Decision.Strategy.ToString(),
                    VideoStrategy = option.Decision.VideoStrategy,
                    AudioStrategy = option.Decision.AudioStrategy,
                    SubtitleStrategy = option.Decision.SubtitleStrategy,
                    Reason = option.Decision.Reason,
                    ClientMaxHeight = clientMaxRes,
                    DeliverableHeight = deliverableMaxRes,
                    OutputHeight = deliveredHeight
                };
                option.Log = log;
                decisionLogs.Add(log);
            }
        }

        if (!options.Any()) throw new InvalidOperationException("No valid media parts or tracks found to stream.");

        var pool = PoolFor(options, wish, settings.StreamingProfile);
        foreach (var option in options)
        {
            if (option.Log != null) option.Log.Considered = pool.Contains(option);
        }

        var bestOption = pool.OrderBy(o => o.PenaltyScore).First();
        if (wish.NarrowsChoice && !bestOption.Exact)
        {
            const string pickReplaced = "Pick: the chosen version or track is not here as it is or would have to be converted, so the closest one that plays was chosen.";
            bestOption.Decision.PickReplaced = true;
            bestOption.Decision.Reason += " " + pickReplaced;
            bestOption.Decision.Reasons.Add(pickReplaced);
        }
        bestOption.Decision.EvaluatedOptions = decisionLogs;
        return (bestOption.Decision, leftAnImageSubtitleToTheClient);
    }

    private static int EmbeddedPosition(IEnumerable<SubtitleStreamInfoDto> tracks, SubtitleStreamInfoDto track) =>
        tracks.Count(other => !other.IsExternal && other.StreamIndex < track.StreamIndex);

    private static List<StreamOption> PoolFor(List<StreamOption> options, PlaybackWish wish, StreamingProfile profile)
    {
        if (!wish.NarrowsChoice) return options;

        var exact = options.Where(o => o.Exact).ToList();
        var family = options.Where(o => o.Family).ToList();
        if (profile != StreamingProfile.ClientPreference)
        {
            bool PlaysAsPicked(StreamOption o) => (wish.Version == null || o.VideoUntouched) && (wish.Audio == null || o.AudioUntouched);
            if (exact.Any(PlaysAsPicked)) return exact;
            var familyThatPlays = family.Where(PlaysAsPicked).ToList();
            if (familyThatPlays.Count > 0) return familyThatPlays;
        }

        if (exact.Count > 0) return exact;
        return family.Count > 0 ? family : options;
    }

    // Parse a video height from MediaPart.Resolution, which is a
    // freeform string that the analyzer produces. Supports common
    // forms: "4K", "UHD", "2160p", "3840x2160", "1440p", "1080p",
    // "1920x1080", "720p", "540p", "480p", "360p", "240p". Matches
    // are substring-based and ordered highest-first so e.g. "1440"
    // can't accidentally hit the "1080" branch on a string like
    // "1440p". Unknown formats fall through to 480 as a conservative
    // baseline (matches the prior implementation's default).
    // Format a height value as the resolution string we surface to
    // clients in the badges / now-playing / watch-history rows. Matches
    // the labels the existing source-side `Resolution` strings use so
    // the web's `'2160p' → '4K'` translation in GlobalVideoPlayer keeps
    // working when reading from `OutputResolution`.
    private static string FrameText(FrameSize? size) => size is { } known ? $"{known.Width}x{known.Height}" : "unknown";

    private static string FormatHeightAsResolution(int height)
    {
        if (height >= 2160) return "2160p";
        if (height >= 1440) return "1440p";
        if (height >= 1080) return "1080p";
        if (height >= 720) return "720p";
        if (height >= 540) return "540p";
        if (height >= 480) return "480p";
        if (height >= 360) return "360p";
        if (height >= 240) return "240p";
        return $"{height}p";
    }

    // The heights the transcoder can actually emit. A client ceiling that falls
    // between rungs (a 1344p phone panel, a 1600p tablet) can only be served at
    // the rung below it, so that is what the scorer must compare against.
    //
    // The rungs are deliberately identical to FormatHeightAsResolution: if the
    // two ever diverge, the resolution the decision declares and the one it
    // scored disagree, which is the whole of the bug this exists to stop.
    private static int SnapToDeliverableHeight(int height)
    {
        if (height >= 2160) return 2160;
        if (height >= 1440) return 1440;
        if (height >= 1080) return 1080;
        if (height >= 720) return 720;
        if (height >= 540) return 540;
        if (height >= 480) return 480;
        if (height >= 360) return 360;
        if (height >= 240) return 240;
        return height;
    }

    private static int LadderKbpsForHeight(int height)
    {
        if (height >= 2160) return 20000;
        if (height >= 1440) return 12000;
        if (height >= 1080) return 8000;
        if (height >= 720) return 4000;
        if (height >= 480) return 2000;
        return 1000;
    }

    private static string? NormalizeHdr(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim().ToUpperInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        if (v is "SDR" or "NONE") return null;
        if (v.Contains("DOVI") || v.Contains("DOLBY")) return "DOLBYVISION";
        if (v.Contains("HDR10PLUS") || v.Contains("HDR10+")) return "HDR10PLUS";
        if (v.Contains("HDR10")) return "HDR10";
        if (v.Contains("HLG")) return "HLG";
        return null;
    }

    private static string? WhyHdrIsNotKept(ClientDevice client, IReadOnlySet<string> hlsVideo, HashSet<string> renderable, bool burnsSubtitleIn, bool gpuEncodesHdr, ServerSetting settings, string sourceCodec)
    {
        if (!settings.PreserveHdrInTranscodes) return "keeping HDR in a transcode is turned off on the server";
        if (settings.EnableHevcEncoding <= 0) return "HEVC encoding is set to Never on the server";
        if (settings.EnableHevcEncoding == 1 && sourceCodec is not ("hevc" or "h265")) return "HEVC encoding is set to HEVC sources only on the server";
        if (burnsSubtitleIn) return "a subtitle is burned into the picture";
        if (!gpuEncodesHdr) return "the server has no GPU that encodes 10-bit HEVC";
        if (!client.SupportedVideoCodecs.Contains("hevc") || !hlsVideo.Contains("hevc")) return "this player cannot take HEVC in a stream";
        if (client.MaxVideoBitDepth is > 0 and < 10) return "this player shows 8-bit video only";
        if (HdrKeptInConversion(renderable, client) == null) return "this player does not say it shows HDR10 or HLG";
        return null;
    }

    private static string? HdrKeptInConversion(HashSet<string> renderable, ClientDevice client)
    {
        if (client.SupportedHdrFormats is not { Count: > 0 } formats) return null;
        var shown = formats.Select(NormalizeHdr).OfType<string>().ToHashSet();
        if (renderable.Contains("HDR10") && (shown.Contains("HDR10") || shown.Contains("HDR10PLUS"))) return "HDR10";
        if (renderable.Contains("HLG") && shown.Contains("HLG")) return "HLG";
        return null;
    }

    // The set of client HDR formats that can natively render a given source. A
    // Dolby Vision Profile 8 source ("DoVi/HDR10" = 8.1, "DoVi/HLG" = 8.4) carries a
    // cross-compatible base layer, so an HDR10- or HLG-capable client can display it
    // even without Dolby Vision support — we return DolbyVision PLUS the base format.
    // A bare "DoVi" (Profile 5, no base) needs true Dolby Vision. HDR10+ is
    // backward-compatible with HDR10 (HDR10 clients ignore the dynamic metadata).
    private static HashSet<string> RenderableHdrFormats(string? raw)
    {
        var set = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(raw)) return set;
        var v = raw.Trim().ToUpperInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        if (v is "SDR" or "NONE") return set;

        if (v.Contains("DOVI") || v.Contains("DOLBY"))
        {
            set.Add("DOLBYVISION");
            if (v.Contains("HDR10PLUS") || v.Contains("HDR10+")) { set.Add("HDR10PLUS"); set.Add("HDR10"); }
            else if (v.Contains("HDR10")) set.Add("HDR10");
            if (v.Contains("HLG")) set.Add("HLG");
        }
        else if (v.Contains("HDR10PLUS") || v.Contains("HDR10+")) { set.Add("HDR10PLUS"); set.Add("HDR10"); }
        else if (v.Contains("HDR10")) set.Add("HDR10");
        else if (v.Contains("HLG")) set.Add("HLG");
        return set;
    }

    // What plays when the client asked for a part but not for a subtitle. Only a
    // forced track turns on by itself; the file's default flag counts only for a
    // profile that chose Default. Smart adds the case a flag cannot express — a
    // title whose own audio is in a language other than the profile's subtitle
    // language, where the viewer needs the full track in that language. A
    // profile that has not chosen a mode gets Smart when the server's
    // AutoEnableSubtitlesForForeignAudio is on, and Only forced otherwise.
    //
    // An audio track with no language tag is left to the forced pick. Most of
    // those are ordinary local rips in the server's own language, and reading
    // "no tag" as "foreign" would put subtitles on half a library.
    public static SubtitleStreamInfoDto? PickAutomaticSubtitle(List<SubtitleStreamInfoDto> tracks, string? audioLanguage, SubtitlePreference preference)
    {
        var inLanguage = tracks.Where(s => MetadataLanguageCodes.SameLanguage(s.Language, preference.Language)).ToList();
        IEnumerable<SubtitleStreamInfoDto> ByHearing(IEnumerable<SubtitleStreamInfoDto> candidates) =>
            candidates.OrderBy(s => s.IsHearingImpaired != preference.PreferHearingImpaired);

        var forced = ByHearing(inLanguage.Where(s => s.IsForced)).FirstOrDefault()
            ?? tracks.FirstOrDefault(s => s.IsForced && MetadataLanguageCodes.SameLanguage(s.Language, audioLanguage))
            ?? tracks.FirstOrDefault(s => s.IsForced && MetadataLanguageCodes.Normalize(s.Language) is null or "und");
        var flagged = forced ?? tracks.FirstOrDefault(s => s.IsDefault);
        var audioIsForeign = MetadataLanguageCodes.Normalize(audioLanguage) != null && !MetadataLanguageCodes.SameLanguage(audioLanguage, preference.Language);

        return preference.Mode switch
        {
            SubtitleMode.Off => null,
            SubtitleMode.ForcedOnly => forced,
            SubtitleMode.Always => ByHearing(inLanguage.Where(s => !s.IsForced)).FirstOrDefault() ?? ByHearing(inLanguage).FirstOrDefault() ?? flagged,
            SubtitleMode.Smart when audioIsForeign => ByHearing(inLanguage.Where(s => !s.IsForced)).FirstOrDefault() ?? ByHearing(inLanguage).FirstOrDefault() ?? forced,
            SubtitleMode.Smart => forced,
            _ => flagged
        };
    }

    private static List<TrackStreamInfoDto> AutomaticAudioTracks(List<TrackStreamInfoDto> tracks, string? preferredLanguage, TrackStreamInfoDto? primary)
    {
        var mainTracks = tracks.Where(track => !PlaybackWish.IsCommentary(track)).ToList();
        var candidates = mainTracks.Count > 0 ? mainTracks : tracks;

        var preferred = candidates.Where(track => MetadataLanguageCodes.SameLanguage(track.Language, preferredLanguage)).ToList();
        if (preferred.Count > 0) return preferred;

        var primaryLanguage = candidates.Where(track => MetadataLanguageCodes.SameLanguage(track.Language, primary?.Language)).ToList();
        return primaryLanguage.Count > 0 ? primaryLanguage : candidates;
    }

    public static bool IsImageSubtitleCodec(string? codec)
    {
        var normalized = codec?.Trim().ToLowerInvariant();
        return normalized is "pgssub" or "hdmv_pgs_subtitle" or "pgs"
            or "dvd_subtitle" or "vobsub"
            or "dvb_subtitle" or "dvbsub"
            or "xsub";
    }

    public static int ParseHeightFromResolution(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return 480;
        if (resolution.Contains("4k", StringComparison.OrdinalIgnoreCase)
            || resolution.Contains("uhd", StringComparison.OrdinalIgnoreCase)
            || resolution.Contains("2160", StringComparison.Ordinal)) return 2160;
        if (resolution.Contains("1440", StringComparison.Ordinal)) return 1440;
        if (resolution.Contains("1080", StringComparison.Ordinal)) return 1080;
        if (resolution.Contains("720", StringComparison.Ordinal)) return 720;
        if (resolution.Contains("540", StringComparison.Ordinal)) return 540;
        if (resolution.Contains("480", StringComparison.Ordinal)) return 480;
        if (resolution.Contains("360", StringComparison.Ordinal)) return 360;
        if (resolution.Contains("240", StringComparison.Ordinal)) return 240;
        return 480;
    }
}
