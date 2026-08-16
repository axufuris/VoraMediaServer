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
    Task<AudioFingerprintResult?> ExtractAudioFingerprintAsync(string filePath, double startSeconds, double lengthSeconds, string workingDirectory, CancellationToken cancellationToken = default);
}
