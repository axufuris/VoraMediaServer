using Vora.Domain.Enums;

namespace Vora.Domain.ValueObjects;

public sealed record HardwareProfile
{
    public List<VideoCodec> SupportedVideoCodecs { get; init; } = new();
    public List<AudioCodec> SupportedAudioCodecs { get; init; } = new();
    public List<string> SupportedContainers { get; init; } = new();
    public List<SubtitleFormat> SupportedSubtitles { get; init; } = new();
    public int MaxBitrateKbps { get; init; }
    public bool SupportsHdr { get; init; }
}
