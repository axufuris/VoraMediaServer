using System.Globalization;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Backups;

public sealed class MusicTrackIdentitySource
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public string? AlbumTitle { get; set; }
    public string? AlbumMusicBrainzId { get; set; }
    public string? ArtistName { get; set; }
}

public sealed class MusicAlbumIdentitySource
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? MusicBrainzId { get; set; }
    public string ArtistName { get; set; } = string.Empty;
}

public sealed class MusicArtistIdentitySource
{
    public Guid Id { get; set; }
    public Guid LibraryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? MusicBrainzId { get; set; }
}

public static class BackupIdentityKeys
{
    public static List<string> ForVideo(ContentIdentitySource s)
    {
        var keys = new List<string>();
        AddKey(keys, ContentIdentity.Compute(s));
        AddKey(keys, ContentIdentity.Compute(s.Type, s.TmdbId, null, null, s.SeasonNumber, s.EpisodeNumber, s.SeriesTmdbId, null, null));
        AddKey(keys, ContentIdentity.Compute(s.Type, null, s.ImdbId, null, s.SeasonNumber, s.EpisodeNumber, null, s.SeriesImdbId, null));
        AddKey(keys, ContentIdentity.Compute(s.Type, null, null, s.TvdbId, s.SeasonNumber, s.EpisodeNumber, null, null, s.SeriesTvdbId));
        AddKey(keys, VideoTitleKey(s));
        return keys;
    }

    public static List<string> ForTrack(MusicTrackIdentitySource s)
    {
        var keys = new List<string>();
        var title = Name(s.Title);
        if (title.Length == 0) return keys;

        var disc = s.DiscNumber ?? 1;
        if (!string.IsNullOrWhiteSpace(s.AlbumMusicBrainzId))
        {
            keys.Add($"track:mbid:{Mbid(s.AlbumMusicBrainzId)}:{disc}:{s.TrackNumber}:{title}");
        }
        keys.Add($"track:name:{Name(s.ArtistName)}|{Name(s.AlbumTitle)}|{disc}|{s.TrackNumber}|{title}");
        return keys;
    }

    public static List<string> ForAlbum(MusicAlbumIdentitySource s)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.MusicBrainzId)) keys.Add($"album:mbid:{Mbid(s.MusicBrainzId)}");
        var title = Name(s.Title);
        if (title.Length > 0) keys.Add($"album:name:{Name(s.ArtistName)}|{title}");
        return keys;
    }

    public static List<string> ForArtist(MusicArtistIdentitySource s)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.MusicBrainzId)) keys.Add($"artist:mbid:{Mbid(s.MusicBrainzId)}");
        var name = Name(s.Name);
        if (name.Length > 0) keys.Add($"artist:name:{name}");
        return keys;
    }

    public static List<string> ForLibrary(string libraryType, string name)
    {
        var normalized = Name(name);
        return normalized.Length == 0 ? new List<string>() : new List<string> { $"library:{libraryType.ToLowerInvariant()}:{normalized}" };
    }

    public static List<string> ForCollection(int? tmdbId, string? imdbId, string? tvdbId, string title)
    {
        var keys = new List<string>();
        if (tmdbId.HasValue) keys.Add($"collection:tmdb:{tmdbId.Value.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(imdbId)) keys.Add($"collection:imdb:{imdbId.Trim()}");
        if (!string.IsNullOrWhiteSpace(tvdbId)) keys.Add($"collection:tvdb:{tvdbId.Trim()}");
        var normalized = Name(title);
        if (normalized.Length > 0) keys.Add($"collection:title:{normalized}");
        return keys;
    }

    public static List<string> ForChannel(Guid playlistId, string externalChannelId) =>
        string.IsNullOrWhiteSpace(externalChannelId)
            ? new List<string>()
            : new List<string> { $"channel:{playlistId:N}:{externalChannelId.Trim()}" };

    private static string? VideoTitleKey(ContentIdentitySource s)
    {
        switch (s.Type)
        {
            case "movie":
            case "show":
            {
                var title = Name(s.Title);
                return title.Length == 0 ? null : $"{s.Type}:title:{title}|{Year(s.ReleaseDate)}";
            }
            case "season":
            {
                var series = Name(s.SeriesTitle);
                return series.Length == 0 || s.SeasonNumber == null ? null : $"season:title:{series}|{Year(s.SeriesReleaseDate)}:{s.SeasonNumber}";
            }
            case "episode":
            {
                var series = Name(s.SeriesTitle);
                return series.Length == 0 || s.SeasonNumber == null || s.EpisodeNumber == null
                    ? null
                    : $"episode:title:{series}|{Year(s.SeriesReleaseDate)}:{s.SeasonNumber}:{s.EpisodeNumber}";
            }
            default:
                return null;
        }
    }

    private static void AddKey(List<string> keys, string? key)
    {
        if (key != null && !keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
    }

    private static string Name(string? value) => MusicNameKey.Normalize(value);

    private static string Mbid(string value) => value.Trim().ToLowerInvariant();

    private static string Year(DateOnly? date) =>
        date.HasValue ? date.Value.Year.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
