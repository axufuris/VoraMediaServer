using Vora.Application.Analysis.Results;
using Vora.Application.Settings;

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

    // When the audio fingerprint already supplied the intro and no recap line was
    // found, the head decode has nothing left to find. Skip it and decode only the
    // tail (credits/preview). Ignored on a single full-file pass.
    public bool SkipHeadWindow { get; set; }

    public bool SamplePictureInTail { get; set; }

    public HardwareAccelerator? HardwareDecoder { get; set; }
}

public interface IMediaAnalyzerService
{
    Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoStreamFacts>> ReadVideoStreamsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaStreamFacts>> ReadStreamFactsAsync(string filePath, CancellationToken cancellationToken = default);
    Task<double?> ProbeMeanVolumeDbAsync(string filePath, CancellationToken cancellationToken = default);
    Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath, SilenceDetectionParameters parameters, CancellationToken cancellationToken = default);
    Task<List<MediaChapter>> ReadChaptersAsync(string filePath, CancellationToken cancellationToken = default);
    Task<AudioFingerprintResult?> ExtractAudioFingerprintAsync(string filePath, double startSeconds, double lengthSeconds, string workingDirectory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubtitleCue>> ReadSubtitleCuesAsync(string filePath, string? externalFilePath, int streamIndex, double windowSeconds, CancellationToken cancellationToken = default);
}
