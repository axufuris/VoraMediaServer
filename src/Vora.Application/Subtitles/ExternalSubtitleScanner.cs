using Microsoft.Extensions.Logging;

namespace Vora.Application.Subtitles;

public interface IExternalSubtitleScanner
{
    IReadOnlyList<ExternalSubtitleFile> Discover(string videoFilePath);
    IReadOnlyList<ExternalSubtitleFile> Match(string videoFilePath, IEnumerable<string> subtitleFilePaths);
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

        try
        {
            return Match(videoFilePath, Directory.EnumerateFiles(directory).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not scan {Directory} for sidecar subtitles.", directory);
            return Array.Empty<ExternalSubtitleFile>();
        }
    }

    public IReadOnlyList<ExternalSubtitleFile> Match(string videoFilePath, IEnumerable<string> subtitleFilePaths)
    {
        var videoFileName = Path.GetFileName(videoFilePath);
        var matches = new List<ExternalSubtitleFile>();
        foreach (var path in subtitleFilePaths.Where(ExternalSubtitleNaming.IsSubtitleExtension))
        {
            if (ExternalSubtitleNaming.TryParse(videoFileName, path) is not { } match) continue;
            if (!SubtitleFileContent.HasText(path))
            {
                _logger.LogInformation("Ignoring the sidecar subtitle {Path}: the file is empty. Replace or delete it.", path);
                continue;
            }
            matches.Add(match);
        }
        return matches.OrderBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
