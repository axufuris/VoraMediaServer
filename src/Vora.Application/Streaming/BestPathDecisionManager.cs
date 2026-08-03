using Vora.Application.Settings;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public interface IBestPathDecisionManager
{
    Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0, Guid? requestedMediaPartId = null);
}

public class BestPathDecisionManager : IBestPathDecisionManager
{
    private readonly ISystemSettingsRepository _settingsRepo;

    public BestPathDecisionManager(ISystemSettingsRepository settingsRepo)
    {
        _settingsRepo = settingsRepo;
    }

    private class StreamOption
    {
        public StreamDecisionDto Decision { get; set; } = new();
        public int PenaltyScore { get; set; }
    }

    public async Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0, Guid? requestedMediaPartId = null)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        var options = new List<StreamOption>();
        var decisionLogs = new List<StreamOptionLogDto>();

        var partsToEvaluate = (requestedMediaPartId.HasValue
            ? mediaItem.Parts.Where(p => p.Id == requestedMediaPartId.Value)
            : mediaItem.Parts).ToList();
        if (partsToEvaluate.Count == 0) partsToEvaluate = mediaItem.Parts.ToList();

        foreach (var part in partsToEvaluate)
        {
            var videoTracksToEvaluate = requestedVideoId.HasValue
                ? part.VideoTracks.Where(v => v.Id == requestedVideoId.Value).ToList()
                : part.VideoTracks;

            var videoTrack = videoTracksToEvaluate.FirstOrDefault(t => t.IsDefault) ?? videoTracksToEvaluate.FirstOrDefault();
            if (videoTrack == null) continue;

            var videoCodec = videoTrack.Codec?.ToLower() ?? "";
            var container = part.Container?.ToLower() ?? "";

            int trackHeight = ParseHeightFromResolution(part.Resolution);
            bool is4k = trackHeight >= 2160;
            int clientMaxRes = requestedMaxResolution > 0 ? requestedMaxResolution : 2160;

            // Effective output height for THIS part once any auto-downscale
            // rules fire. Used by the resolution-fit penalty below so the
            // scorer can compare "what does the user actually see if we
            // pick this part?" across all candidates. We compute it now
            // because the HDR auto-downscale check needs the video
            // track's HdrType + the admin setting.
            bool isHdrPart = !string.IsNullOrWhiteSpace(videoTrack.HdrType)
                && !string.Equals(videoTrack.HdrType, "SDR", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(videoTrack.HdrType, "None", StringComparison.OrdinalIgnoreCase);
            int effectiveTargetHeight = clientMaxRes;
            if (isHdrPart && string.Equals(settings.HdrTranscodeDownscale, "Always", StringComparison.OrdinalIgnoreCase))
            {
                effectiveTargetHeight = Math.Min(effectiveTargetHeight, 1080);
            }
            int outputHeight = Math.Min(trackHeight, effectiveTargetHeight);

            SubtitleStreamInfoDto? subTrack = null;
            if (requestedSubtitleId.HasValue)
            {
                if (requestedSubtitleId.Value != Guid.Empty)
                    subTrack = part.SubtitleTracks.FirstOrDefault(s => s.Id == requestedSubtitleId.Value);
            }
            else
            {
                subTrack = part.SubtitleTracks.FirstOrDefault(s => s.IsForced) ?? part.SubtitleTracks.FirstOrDefault(s => s.IsDefault);
            }

            var subCodec = subTrack?.Codec?.ToLower() ?? "";
            bool isImageSubtitle = subCodec == "pgssub" || subCodec == "hdmv_pgs_subtitle" || subCodec == "dvd_subtitle" || subCodec == "vobsub";
            bool requiresBurnIn = subTrack != null && isImageSubtitle;

            var primaryAudioTrack = part.AudioTracks.FirstOrDefault(t => t.IsDefault) ?? part.AudioTracks.FirstOrDefault();

            var audioTracksToEvaluate = requestedAudioId.HasValue
                ? part.AudioTracks.Where(a => a.Id == requestedAudioId.Value).ToList()
                : part.AudioTracks;

            if (!audioTracksToEvaluate.Any() && requestedAudioId.HasValue) audioTracksToEvaluate = part.AudioTracks;

            int bitrateKbps = (int)((part.OverallBitrate ?? 3000000) / 1000);
            bool exceedsBandwidth = maxAllowedBandwidthKbps > 0 && bitrateKbps > maxAllowedBandwidthKbps;

            var sourceHdr = NormalizeHdr(videoTrack.HdrType);
            bool hdrNotRenderable = false;
            if (sourceHdr != null && client.SupportedHdrFormats != null)
            {
                var clientHdr = client.SupportedHdrFormats
                    .Select(NormalizeHdr)
                    .Where(h => h != null)
                    .ToHashSet();
                // TODO(DoVi profile): once the DoVi profile is surfaced, let a Profile 8
                // ("DoVi/HDR10") source fall back to its HDR10 base layer for HDR10-capable
                // clients instead of tonemapping. v1 treats unlisted DoVi as not-renderable.
                hdrNotRenderable = !clientHdr.Contains(sourceHdr);
            }

            bool bitDepthTooHigh = client.MaxVideoBitDepth > 0 && (videoTrack.BitDepth ?? 8) > client.MaxVideoBitDepth;

            foreach (var audioTrack in audioTracksToEvaluate)
            {
                var audioCodec = audioTrack.Codec?.ToLower() ?? "";
                int trackChannels = audioTrack.Channels ?? 2;
                bool isPrimaryAudio = audioTrack.Id == primaryAudioTrack?.Id;

                bool codecUnsupported = !string.IsNullOrEmpty(videoCodec) && !client.SupportedVideoCodecs.Contains(videoCodec);
                bool needsVideoTranscode = codecUnsupported || requiresBurnIn || exceedsBandwidth || hdrNotRenderable || bitDepthTooHigh;
                bool needsAudioDownmix = trackChannels > client.MaxAudioChannels;
                bool needsAudioTranscode = (!string.IsNullOrEmpty(audioCodec) && !client.SupportedAudioCodecs.Contains(audioCodec)) || needsAudioDownmix;
                bool needsRemux = !string.IsNullOrEmpty(container) && !client.SupportedContainers.Contains(container);

                var reasons = new List<string>();

                if (trackHeight > clientMaxRes)
                {
                    needsVideoTranscode = true;
                    reasons.Add($"Video Transcode: Track resolution ({trackHeight}p) exceeds client maximum ({clientMaxRes}p).");
                }

                if ((needsVideoTranscode || needsAudioTranscode) && videoCodec == "hevc" && client.DeviceType == "Browser")
                {
                    needsVideoTranscode = true;
                }

                var option = new StreamOption
                {
                    Decision = new StreamDecisionDto
                    {
                        SelectedMediaPartId = part.Id,
                        SelectedVideoTrackId = videoTrack.Id,
                        SelectedAudioTrackId = audioTrack.Id,
                        SelectedSubtitleTrackId = subTrack?.Id,
                        RequiresSubtitleBurnIn = requiresBurnIn,
                        TargetAudioChannels = needsAudioDownmix ? client.MaxAudioChannels : trackChannels,
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

                    if (settings.StreamingProfile == StreamingProfile.DirectStreamPreference) currentScore += 50000;
                    else if (settings.StreamingProfile == StreamingProfile.ClientPreference) currentScore += 100;
                    else if (settings.StreamingProfile == StreamingProfile.BandwidthOptimized) currentScore += (100 + bitrateKbps);

                    if (needsVideoTranscode)
                    {
                        option.Decision.VideoStrategy = "Transcode";
                        option.Decision.TargetVideoCodec = "h264";

                        if (requiresBurnIn) reasons.Add($"Video Transcode: Subtitle burn-in required ({subCodec}).");
                        else if (exceedsBandwidth) reasons.Add($"Video Transcode: Bitrate ({(bitrateKbps / 1000.0):F1} Mbps) exceeds {bandwidthLimitSource} ({(maxAllowedBandwidthKbps / 1000.0):F1} Mbps).");
                        else if (videoCodec == "hevc" && client.DeviceType == "Browser") reasons.Add("Video Transcode: HEVC unsupported in HLS for Browsers.");
                        else if (trackHeight > clientMaxRes) { }
                        else if (codecUnsupported) reasons.Add($"Video Transcode: '{videoCodec}' unsupported.");

                        if (hdrNotRenderable) reasons.Add($"Video Transcode: Source HDR ({videoTrack.HdrType}) not supported by client; tonemapping to SDR.");
                        if (bitDepthTooHigh) reasons.Add($"Video Transcode: Source bit depth ({videoTrack.BitDepth}-bit) exceeds client maximum ({client.MaxVideoBitDepth}-bit).");

                        currentScore += is4k ? 200 : 50;
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
                        option.Decision.TargetAudioCodec = "aac";

                        if (needsAudioDownmix) reasons.Add($"Audio Transcode: Downmix required ({trackChannels}ch > {client.MaxAudioChannels}ch).");
                        else reasons.Add($"Audio Transcode: '{audioCodec}' unsupported.");

                        currentScore += 10;
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
                    option.Decision.TargetContainer = "mp4";
                    reasons.Add($"Remux: Container '{container}' unsupported. Remuxing to mp4.");
                    currentScore += 5;

                    if (settings.StreamingProfile == StreamingProfile.BandwidthOptimized) currentScore += bitrateKbps;
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

                    if (settings.StreamingProfile == StreamingProfile.BandwidthOptimized) currentScore += bitrateKbps;
                }

                if (settings.StreamingProfile == StreamingProfile.BandwidthOptimized && trackHeight >= 2160)
                {
                    currentScore += 20000;
                }

                // Resolution-fit penalty. Two components added to the
                // score so the lowest-penalty option wins:
                //
                //   qualityLossPenalty (heavy, weight 10):
                //     punishes parts whose output is below what the
                //     user/client asked for. clientMaxRes is the
                //     ceiling (RequestedMaxResolution or 2160 default);
                //     outputHeight is what this part can actually
                //     deliver under the current HDR-downscale rules.
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
                int qualityLossPenalty = Math.Max(0, clientMaxRes - outputHeight) * 10;
                int downscaleWorkPenalty = Math.Max(0, trackHeight - outputHeight);
                currentScore += qualityLossPenalty + downscaleWorkPenalty;

                if (audioTrack.IsDefault) currentScore -= 2;

                if (!needsAudioDownmix && trackChannels < client.MaxAudioChannels)
                {
                    currentScore += (client.MaxAudioChannels - trackChannels);
                }

                if (!string.IsNullOrEmpty(audioTrack.Title) && audioTrack.Title.Contains("commentary", StringComparison.OrdinalIgnoreCase))
                {
                    currentScore += 500;
                    reasons.Add("[Commentary Track Penalized]");
                }

                int finalBandwidthKbps = exceedsBandwidth ? maxAllowedBandwidthKbps : bitrateKbps;
                if (settings.StreamingProfile == StreamingProfile.BandwidthOptimized && (needsVideoTranscode || needsAudioTranscode))
                {
                    finalBandwidthKbps = Math.Min(finalBandwidthKbps, LadderKbpsForHeight(outputHeight));
                }
                double finalMbps = finalBandwidthKbps / 1000.0;

                option.Decision.Reason = string.Join(" ", reasons);
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
                    option.Decision.OutputHdrType = "SDR";
                }
                else
                {
                    option.Decision.OutputResolution = part.Resolution ?? string.Empty;
                    option.Decision.OutputHdrType = string.IsNullOrWhiteSpace(videoTrack.HdrType) ? "SDR" : videoTrack.HdrType;
                }

                option.PenaltyScore = currentScore;

                options.Add(option);

                decisionLogs.Add(new StreamOptionLogDto
                {
                    MediaPartId = part.Id,
                    PenaltyScore = option.PenaltyScore,
                    Strategy = option.Decision.Strategy.ToString(),
                    VideoStrategy = option.Decision.VideoStrategy,
                    AudioStrategy = option.Decision.AudioStrategy,
                    SubtitleStrategy = option.Decision.SubtitleStrategy,
                    Reason = option.Decision.Reason
                });
            }
        }

        if (!options.Any()) throw new InvalidOperationException("No valid media parts or tracks found to stream.");

        var bestOption = options.OrderBy(o => o.PenaltyScore).First();
        bestOption.Decision.EvaluatedOptions = decisionLogs;
        return bestOption.Decision;
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