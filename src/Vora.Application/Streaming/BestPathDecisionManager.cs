using Vora.Application.Settings;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public interface IBestPathDecisionManager
{
    Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0);
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

    public async Task<StreamDecisionDto> DetermineBestPathAsync(ClientDevice client, MediaStreamInfoDto mediaItem, int maxAllowedBandwidthKbps, string bandwidthLimitSource, Guid? requestedVideoId = null, Guid? requestedAudioId = null, Guid? requestedSubtitleId = null, int requestedMaxResolution = 0)
    {
        var settings = await _settingsRepo.GetSettingsAsync();
        var options = new List<StreamOption>();
        var decisionLogs = new List<StreamOptionLogDto>();

        foreach (var part in mediaItem.Parts)
        {
            var videoTracksToEvaluate = requestedVideoId.HasValue
                ? part.VideoTracks.Where(v => v.Id == requestedVideoId.Value).ToList()
                : part.VideoTracks;

            var videoTrack = videoTracksToEvaluate.FirstOrDefault(t => t.IsDefault) ?? videoTracksToEvaluate.FirstOrDefault();
            if (videoTrack == null) continue;

            var videoCodec = videoTrack.Codec?.ToLower() ?? "";
            var container = part.Container?.ToLower() ?? "";

            bool is4k = part.Resolution?.Contains("4k", StringComparison.OrdinalIgnoreCase) == true || part.Resolution?.Contains("2160") == true;
            int trackHeight = is4k ? 2160 : part.Resolution?.Contains("1080") == true ? 1080 : part.Resolution?.Contains("720") == true ? 720 : 480;
            int clientMaxRes = requestedMaxResolution > 0 ? requestedMaxResolution : 2160;

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

            foreach (var audioTrack in audioTracksToEvaluate)
            {
                var audioCodec = audioTrack.Codec?.ToLower() ?? "";
                int trackChannels = audioTrack.Channels ?? 2;
                bool isPrimaryAudio = audioTrack.Id == primaryAudioTrack?.Id;

                bool needsVideoTranscode = (!string.IsNullOrEmpty(videoCodec) && !client.SupportedVideoCodecs.Contains(videoCodec)) || requiresBurnIn || exceedsBandwidth;
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
                        else reasons.Add($"Video Transcode: '{videoCodec}' unsupported.");

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
                double finalMbps = finalBandwidthKbps / 1000.0;

                option.Decision.Reason = string.Join(" ", reasons);
                option.Decision.BandwidthKbps = finalBandwidthKbps;
                option.Decision.Quality = (needsVideoTranscode || needsAudioTranscode) ? $"Transcode ({finalMbps:F1} Mbps)" : $"Original ({finalMbps:F1} Mbps)";
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
}