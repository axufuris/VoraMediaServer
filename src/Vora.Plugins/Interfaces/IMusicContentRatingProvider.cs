namespace Vora.Plugins.Interfaces;

// Supplies explicit / clean flags for music whose files carry no advisory tag.
// Two lookups, because they differ in how much they can be trusted: an ISRC
// names one recording exactly, while an album search returns every edition with
// that title — explicit and clean alike — and cannot say which one the file is.
public interface IMusicContentRatingProvider : IVoraPlugin
{
    Task<TrackAdvisoryLookup> GetTrackAdvisoryByIsrcAsync(string isrc, CancellationToken cancellationToken);

    Task<AlbumEditionsLookup> GetAlbumEditionsAsync(string artistName, string albumTitle, CancellationToken cancellationToken);
}

public enum ContentRatingLookupOutcome
{
    Found = 0,

    // The provider answered and does not know the recording or album.
    NotFound = 1,

    // No answer: offline, rate-limited or erroring. Retry later, and stop the run.
    Unavailable = 2,
}

public enum ProviderAdvisory
{
    // The provider has no opinion. Never read as clean.
    Unknown = 0,
    Clean = 1,
    Explicit = 2,
}

public sealed class TrackAdvisoryLookup
{
    public ContentRatingLookupOutcome Outcome { get; init; }
    public ProviderAdvisory Advisory { get; init; }

    public static TrackAdvisoryLookup NotFound { get; } = new() { Outcome = ContentRatingLookupOutcome.NotFound };
    public static TrackAdvisoryLookup Unavailable { get; } = new() { Outcome = ContentRatingLookupOutcome.Unavailable };
}

public sealed class AlbumEditionsLookup
{
    public ContentRatingLookupOutcome Outcome { get; init; }
    public IReadOnlyList<ProviderAlbumEdition> Editions { get; init; } = Array.Empty<ProviderAlbumEdition>();

    public static AlbumEditionsLookup NotFound { get; } = new() { Outcome = ContentRatingLookupOutcome.NotFound };
    public static AlbumEditionsLookup Unavailable { get; } = new() { Outcome = ContentRatingLookupOutcome.Unavailable };
}

public sealed class ProviderAlbumEdition
{
    public required string Title { get; init; }
    public required string ArtistName { get; init; }
    public IReadOnlyList<ProviderEditionTrack> Tracks { get; init; } = Array.Empty<ProviderEditionTrack>();
}

public sealed class ProviderEditionTrack
{
    public required string Title { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Isrc { get; init; }
    public ProviderAdvisory Advisory { get; init; }
}
