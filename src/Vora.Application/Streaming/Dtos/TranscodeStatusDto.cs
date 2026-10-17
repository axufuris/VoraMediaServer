namespace Vora.Application.Streaming.Dtos;

public sealed record TranscodeStatusDto(
    string? VideoEncoder,
    string? EncodeHardware,
    string? DecodeHardware,
    int? VideoBitrateKbps,
    int? VideoPeakKbps,
    string? ToneMapping,
    string? AudioEncoder,
    int? AudioChannels,
    int? AudioBitrateKbps,
    double? Fps,
    double? Speed,
    bool Running,
    bool RetriedOnCpu,
    bool Paused);
