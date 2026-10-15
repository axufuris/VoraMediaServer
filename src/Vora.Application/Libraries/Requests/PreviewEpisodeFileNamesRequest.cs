namespace Vora.Application.Libraries.Requests;

public class PreviewEpisodeFileNamesRequest
{
    public List<string> FolderPaths { get; set; } = new();
    public string? ScannerRegex { get; set; }
    public List<string>? ExcludeFilters { get; set; }
}
