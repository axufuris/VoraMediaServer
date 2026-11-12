namespace Vora.Domain.Entities.Users;

// One recommendation provider's For You rows for a profile, as the provider
// last built them. For You reads these instead of asking the provider while
// the page waits (docs/for-you.md). Only the rows' titles and the ids of their
// titles are kept: the tiles are loaded when the page is read, so the viewer's
// access and watch state are always current.
public class RecommendationSnapshot
{
    // The scope of Home's For You, which spans every library. A library's For
    // You tab uses the library's id. Not null, so the unique key needs nothing
    // newer than Postgres 15's NULLS NOT DISTINCT.
    public static readonly Guid AllLibraries = Guid.Empty;

    public Guid ProfileId { get; set; }
    public Guid LibraryScope { get; set; }
    public string ProviderId { get; set; } = string.Empty;

    // The provider's rows as a JSON array of RecommendationListDto.
    public string Rows { get; set; } = "[]";

    public DateTime GeneratedAt { get; set; }

    public static Guid ScopeOf(Guid? libraryId) => libraryId ?? AllLibraries;
}
