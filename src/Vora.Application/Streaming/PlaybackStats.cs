using System.Globalization;
using Vora.Application.Analysis.Results;
using Vora.Application.Streaming.Dtos;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public static class PlaybackStats
{
    public static PlaybackStatsVM Build(StreamSession session, MediaPart? part, IReadOnlyList<MediaStreamFacts> facts, TranscodeStatusDto? transcode)
    {
        var directPlay = session.Strategy == nameof(StreamStrategy.DirectPlay);
        var video = part?.VideoTracks.FirstOrDefault(t => t.Id == session.VideoTrackId) ?? part?.VideoTracks.FirstOrDefault();
        var audio = part?.AudioTracks.FirstOrDefault(t => t.Id == session.AudioTrackId);
        var subtitle = session.SubtitleTrackId is Guid subtitleId ? part?.SubtitleTracks.FirstOrDefault(t => t.Id == subtitleId) : null;
        var videoFacts = video == null ? null : facts.FirstOrDefault(f => f.StreamIndex == video.StreamIndex && f.Kind == "video");
        var audioFacts = audio == null || audio.StreamIndex < 0 ? null : facts.FirstOrDefault(f => f.StreamIndex == audio.StreamIndex && f.Kind == "audio");

        return new PlaybackStatsVM
        {
            Method = session.Strategy,
            Delivery = directPlay ? "File" : "HLS",
            SourceContainer = part?.Container,
            SourceBitrateKbps = Kilobits(part?.OverallBitrate),
            StreamBitrateKbps = directPlay || session.BandwidthKbps <= 0 ? null : session.BandwidthKbps,
            Video = video == null && string.IsNullOrEmpty(session.VideoStrategy) ? null : new PlaybackVideoStatsVM
            {
                Codec = video?.Codec,
                Profile = video?.Profile ?? videoFacts?.Profile,
                Level = Level(video?.Codec ?? videoFacts?.Codec, videoFacts?.Level),
                BitDepth = video?.BitDepth,
                HdrType = video?.HdrType,
                Width = video?.Width,
                Height = video?.Height,
                FrameRate = video?.FrameRate,
                BitrateKbps = Kilobits(video?.Bitrate ?? videoFacts?.BitRate),
                Strategy = session.VideoStrategy,
                OutputCodec = string.IsNullOrEmpty(session.VideoCodec) ? null : session.VideoCodec,
                OutputWidth = session.OutputWidth,
                OutputHeight = session.OutputHeight,
                OutputHdrType = session.OutputHdrType,
                OutputBitrateKbps = transcode?.VideoBitrateKbps,
                OutputPeakKbps = transcode?.VideoPeakKbps,
                ToneMapping = transcode?.ToneMapping,
            },
            Audio = audio == null ? null : new PlaybackAudioStatsVM
            {
                Codec = audio.Codec,
                Profile = audioFacts?.Profile,
                Channels = audio.Channels,
                ChannelLayout = audioFacts?.ChannelLayout,
                SampleRate = audioFacts?.SampleRate,
                Language = audio.Language,
                Title = audio.Title,
                IsDefault = audio.IsDefault,
                BitrateKbps = Kilobits(audioFacts?.BitRate),
                Strategy = session.AudioStrategy,
                OutputCodec = string.IsNullOrEmpty(session.AudioCodec) ? null : session.AudioCodec,
                OutputChannels = transcode?.AudioChannels ?? (session.TargetAudioChannels > 0 ? session.TargetAudioChannels : null),
                OutputBitrateKbps = transcode?.AudioBitrateKbps,
            },
            Subtitle = subtitle == null ? null : new PlaybackSubtitleStatsVM
            {
                Codec = subtitle.Codec,
                Language = subtitle.Language,
                Title = subtitle.Title,
                IsForced = subtitle.IsForced,
                IsExternal = subtitle.IsExternal,
                Strategy = session.SubtitleStrategy,
            },
            Transcoder = transcode == null ? null : new PlaybackTranscoderStatsVM
            {
                Encoder = transcode.VideoEncoder,
                EncodeHardware = transcode.EncodeHardware,
                DecodeHardware = transcode.DecodeHardware,
                Fps = transcode.Fps,
                Speed = transcode.Speed,
                Running = transcode.Running,
                RetriedOnCpu = transcode.RetriedOnCpu,
            },
        };
    }

    public static string? Level(string? codec, int? level)
    {
        if (level is not int value || value <= 0) return null;
        return codec?.ToLowerInvariant() switch
        {
            "h264" or "avc" => (value / 10.0).ToString("0.#", CultureInfo.InvariantCulture),
            "hevc" or "h265" => (value / 30.0).ToString("0.#", CultureInfo.InvariantCulture),
            "av1" => $"{2 + (value >> 2)}.{value & 3}",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static int? Kilobits(long? bitsPerSecond) =>
        bitsPerSecond is long value && value > 0 ? (int)Math.Round(value / 1000.0) : null;
}
