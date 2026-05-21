using System.Text.Json.Serialization;

namespace Vora.Application.Media.SmartPlaylists;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartPlaylistField
{
    Title,
    Artist,
    AlbumTitle,
    AlbumArtist,
    Genre,
    Year,
    DurationSeconds,
    ContentRating,
    PlayCount,
    LastPlayedAt,
    DateAdded,
    Liked,
    TrackNumber,
    DiscNumber,
    LibraryId,
    IsCompilation,
    ReleaseYear,
    ShowTitle,
    SeasonNumber,
    EpisodeNumber,
    IsWatched,
    Rating,
    ServerAdminRating,
    MyRating,
    AudienceRating
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartPlaylistOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    Between,
    InLastDays,
    NotInLastDays,
    IsNull,
    IsNotNull
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartPlaylistSortBy
{
    Random,
    Title,
    ArtistName,
    AlbumTitle,
    Year,
    DateAdded,
    LastPlayedAt,
    PlayCount,
    DurationSeconds
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartPlaylistSortDirection
{
    Asc,
    Desc
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmartPlaylistMatch
{
    All,
    Any
}

public sealed class SmartPlaylistRuleGroup
{
    public SmartPlaylistMatch Match { get; set; } = SmartPlaylistMatch.All;
    public List<SmartPlaylistRule> Rules { get; set; } = new();
    public List<SmartPlaylistRuleGroup> Groups { get; set; } = new();
}

public sealed class SmartPlaylistRule
{
    public SmartPlaylistField Field { get; set; }
    public SmartPlaylistOperator Operator { get; set; }
    public string? Value { get; set; }
    public string? SecondValue { get; set; }
}

public sealed class SmartPlaylistDefinition
{
    public SmartPlaylistRuleGroup Root { get; set; } = new();
    public int? Limit { get; set; }
    public SmartPlaylistSortBy SortBy { get; set; } = SmartPlaylistSortBy.Random;
    public SmartPlaylistSortDirection SortDirection { get; set; } = SmartPlaylistSortDirection.Asc;
}
