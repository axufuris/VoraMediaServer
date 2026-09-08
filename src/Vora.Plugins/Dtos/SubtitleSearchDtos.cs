namespace Vora.Plugins.Dtos;

public class SubtitleSearchQuery
{
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }

    // Set together for an episode; both null for a movie.
    public int? Season { get; set; }
    public int? Episode { get; set; }

    public IReadOnlyList<string> Languages { get; set; } = Array.Empty<string>();

    public bool IsEpisode => Season.HasValue && Episode.HasValue;
}

public class SubtitleSearchResultDto
{
    public string ProviderId { get; set; } = string.Empty;

    // Opaque to Vora: whatever the provider needs handed back to fetch this file.
    public string ProviderFileId { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;
    public string? Language { get; set; }

    // srt / ass / vtt — the extension the bytes are in, not a container.
    public string? Format { get; set; }

    public bool HearingImpaired { get; set; }
    public bool Forced { get; set; }
    public int? DownloadCount { get; set; }
    public string? Uploader { get; set; }
    public decimal? Rating { get; set; }
}

public class SubtitleDownloadDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "srt";
    public string? Language { get; set; }
    public string? FileName { get; set; }
}
