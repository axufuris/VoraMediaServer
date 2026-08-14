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
}

public interface IMediaAnalyzerService
{
    Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath);
    Task<double?> ProbeMeanVolumeDbAsync(string filePath, CancellationToken cancellationToken = default);
    Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath, SilenceDetectionParameters parameters, CancellationToken cancellationToken = default);
}
