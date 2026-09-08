using Microsoft.Extensions.Logging;

namespace Vora.Application.Subtitles;

public interface IExternalSubtitleScanner
{
    IReadOnlyList<ExternalSubtitleFile> Discover(string videoFilePath);
}

public class ExternalSubtitleScanner : IExternalSubtitleScanner
{
    private readonly ILogger<ExternalSubtitleScanner> _logger;

    public ExternalSubtitleScanner(ILogger<ExternalSubtitleScanner> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ExternalSubtitleFile> Discover(string videoFilePath)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath)) return Array.Empty<ExternalSubtitleFile>();

        var directory = Path.GetDirectoryName(videoFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return Array.Empty<ExternalSubtitleFile>();

        var videoFileName = Path.GetFileName(videoFilePath);

        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(ExternalSubtitleNaming.IsSubtitleExtension)
                .Select(path => ExternalSubtitleNaming.TryParse(videoFileName, path))
                .Where(match => match != null)
                .Select(match => match!)
                .OrderBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not scan {Directory} for sidecar subtitles.", directory);
            return Array.Empty<ExternalSubtitleFile>();
        }
    }
}
