using Vora.Application.Analysis.Results;

namespace Vora.Application.Analysis;

public class SilenceDetectionParameters
{
    public double NoiseThresholdDb { get; set; } = -40;
    public double MinSilenceDurationSec { get; set; } = 1.5;
    public double MinBlackFrameDurationSec { get; set; } = 0.5;

    // When both are set, detection decodes only [0, HeadWindowEndSeconds] and
    // [TailWindowStartSeconds, end] instead of the whole file. Null (either) →
    // single full-file pass. The assembler reads gaps only from those two
    // regions, so windowing is output-equivalent while skipping the middle decode.
    public double? HeadWindowEndSeconds { get; set; }
    public double? TailWindowStartSeconds { get; set; }

    // When a higher tier (audio fingerprint / chapters) already supplied the intro,
    // the head decode has nothing left to find — recap and the silence/black intro
    // are the only things it produces, and both are superseded. Skip it and decode
    // only the tail (credits/preview). Ignored on a single full-file pass.
    public bool SkipHeadWindow { get; set; }

    // Decode on the GPU (NVDEC via -hwaccel) when the server has hardware
    // acceleration enabled — 10-bit HEVC (~all of a modern library) decodes far
    // faster there. Frames still land in system memory for the CPU black/silence
    // filters; on any hardware failure the pass retries in software.
    public bool UseHardwareDecode { get; set; }
    public string? HardwareDevice { get; set; }
}

public interface IMediaAnalyzerService
{
    Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<double?> ProbeMeanVolumeDbAsync(string filePath, CancellationToken cancellationToken = default);
    Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath, SilenceDetectionParameters parameters, CancellationToken cancellationToken = default);
    Task<List<MediaChapter>> ReadChaptersAsync(string filePath, CancellationToken cancellationToken = default);
    Task<AudioFingerprintResult?> ExtractAudioFingerprintAsync(string filePath, double startSeconds, double lengthSeconds, string workingDirectory, CancellationToken cancellationToken = default);
}
