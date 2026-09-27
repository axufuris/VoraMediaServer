namespace Vora.Application.Subtitles.ViewModels;

public class SubtitleSearchResultVM
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderFileId { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? Format { get; set; }
    public bool HearingImpaired { get; set; }
    public bool Forced { get; set; }
    public int? DownloadCount { get; set; }
    public string? Uploader { get; set; }
    public decimal? Rating { get; set; }
}

public class DownloadedSubtitleVM
{
    public Guid Id { get; set; }
    public Guid MediaPartId { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public string? Codec { get; set; }
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsDefault { get; set; }
}

public class SubtitleSearchFactsDto
{
    public string Title { get; set; } = string.Empty;
    public string? SeriesTitle { get; set; }
    public int? Year { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}

public class DownloadSubtitleRequest
{
    public string ProviderFileId { get; set; } = string.Empty;
    public string? Language { get; set; }

    // Echoed back from the search row the viewer picked. The provider's download
    // response carries only the bytes, so this is the only place the traits of
    // the file being attached are known.
    public bool HearingImpaired { get; set; }
    public bool Forced { get; set; }
}
