using Vora.Application.Analysis.Results;

namespace Vora.Application.Analysis;

public class SilenceDetectionParameters
{
    public double NoiseThresholdDb { get; set; } = -40;
    public double MinSilenceDurationSec { get; set; } = 1.5;
    public double MinBlackFrameDurationSec { get; set; } = 0.5;
}

public interface IMediaAnalyzerService
{
    Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath);
    Task<double?> ProbeMeanVolumeDbAsync(string filePath);
    Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath, SilenceDetectionParameters parameters);
}
