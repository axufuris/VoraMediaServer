namespace Vora.Application.Media.ViewModels;

public class ArtistVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }
    public string? Biography { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ClearLogoUrl { get; set; }
    public Guid LibraryId { get; set; }
    public List<string> LockedFields { get; set; } = new();
}

public class AlbumVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? DiscArtUrl { get; set; }
    public string? AlbumArtist { get; set; }
    public bool IsCompilation { get; set; }
    public Guid ArtistId { get; set; }
    public string ArtistName { get; set; } = string.Empty;
    public List<string> LockedFields { get; set; } = new();
}

public class TrackVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Artist { get; set; }
    public int TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContentRating { get; set; }
    public Guid? AlbumId { get; set; }
    public bool IsLiked { get; set; }
    public List<string> LockedFields { get; set; } = new();
}

public class GenreSummaryVM
{
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public int ArtistCount { get; set; }
    public string? SampleArtworkUrl { get; set; }
}

public class GenreContentVM
{
    public string Name { get; set; } = string.Empty;
    public List<ArtistVM> Artists { get; set; } = new();
    public List<AlbumVM> Albums { get; set; } = new();
    public List<TrackVM> Tracks { get; set; } = new();
}

public class AdminMusicHistoryRowVM
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public Guid TrackId { get; set; }
    public string TrackTitle { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public DateTime PlayedAt { get; set; }
    public int DurationListenedSeconds { get; set; }
    public bool Completed { get; set; }
}

public class AdminMusicHistoryVM
{
    public List<AdminMusicHistoryRowVM> Rows { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AdminTopTrackVM
{
    public Guid TrackId { get; set; }
    public string TrackTitle { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public int PlayCount { get; set; }
}

public class AdminTopArtistVM
{
    public Guid ArtistId { get; set; }
    public string ArtistName { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public int PlayCount { get; set; }
}

public class AdminProfilePlayCountVM
{
    public Guid ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public int PlayCount { get; set; }
}

public class AdminMusicSummaryVM
{
    public int TotalPlays { get; set; }
    public int DistinctProfileCount { get; set; }
    public List<AdminTopTrackVM> TopTracks { get; set; } = new();
    public List<AdminTopArtistVM> TopArtists { get; set; } = new();
    public List<AdminProfilePlayCountVM> PlaysPerProfile { get; set; } = new();
}

public class ArtistTrackVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public int TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContentRating { get; set; }
    public Guid? AlbumId { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumArtworkUrl { get; set; }
    public bool IsLiked { get; set; }
}
