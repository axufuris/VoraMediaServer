using Vora.Application.Analysis.Results;

namespace Vora.Application.Analysis;

public interface IMediaAnalyzerService
{
    Task<MediaAnalysisResult> AnalyzeFileAsync(string filePath);
    Task<MediaAnalysisResult> AnalyzeSilenceDetectionsAsync(string filePath);
}