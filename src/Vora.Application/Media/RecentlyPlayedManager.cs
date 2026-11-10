using Vora.Application.Media.Ai;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Media.ViewModels;
using Vora.Application.Playlists;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Enums;

namespace Vora.Application.Media;

public interface IRecentlyPlayedManager
{
    Task<List<RecentlyPlayedItemVM>> GetAsync(Guid profileId, PlaylistAccessFilter access, int limit);
}

// Recently Played on Music's For You: what the profile last played from. A
// playlist, smart playlist or mix (AI playlists included) is one tile however
// many of its songs were heard; any other play is its song, shown with its album.
public class RecentlyPlayedManager(
    IMusicRepository music,
    IMusicRecommendationRepository mixes,
    IAiPlaylistRepository aiPlaylists,
    IPlaylistRepository playlists,
    ISmartPlaylistRepository smartPlaylists) : IRecentlyPlayedManager
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    // How far back to look. An evening on one playlist is a hundred plays of a
    // single tile, so the row needs more than its own length of history.
    internal const int PlaysScanned = 500;

    public async Task<List<RecentlyPlayedItemVM>> GetAsync(Guid profileId, PlaylistAccessFilter access, int limit)
    {
        var take = Math.Clamp(limit, 1, MaxLimit);
        var plays = await music.GetRecentPlaysAsync(profileId, PlaysScanned);

        // Twice the row, so tiles left out below (a weekly AI playlist since
        // replaced, a playlist since deleted or unshared, a song no longer
        // allowed) still leave a full row where there is more history.
        var entries = Entries(plays, take * 2);
        if (entries.Count == 0) return new List<RecentlyPlayedItemVM>();

        List<Guid> IdsOf(RecentlyPlayedKind kind) => entries.Where(e => e.Kind == kind).Select(e => e.Id).ToList();

        var tracks = (await music.GetTracksByIdsAsync(IdsOf(RecentlyPlayedKind.Track), access.Music)).ToDictionary(t => t.Id);
        var liked = tracks.Count == 0 ? new HashSet<Guid>() : await music.GetLikedTrackIdsAsync(profileId, tracks.Keys);

        var mixRows = (await mixes.GetMixesByIdsAsync(IdsOf(RecentlyPlayedKind.Mix), profileId)).ToDictionary(m => m.Id);
        var mixArt = await MixArtAsync(mixRows.Values);

        var playlistRows = (await playlists.GetVisibleSummariesAsync(IdsOf(RecentlyPlayedKind.Playlist), profileId, access)).ToDictionary(p => p.Id);

        var smartRows = new Dictionary<Guid, SmartPlaylist>();
        foreach (var id in IdsOf(RecentlyPlayedKind.SmartPlaylist))
        {
            if (await smartPlaylists.GetVisibleAsync(id, profileId) is { } smart) smartRows[id] = smart;
        }

        var items = new List<RecentlyPlayedItemVM>();
        foreach (var entry in entries)
        {
            var item = entry.Kind switch
            {
                RecentlyPlayedKind.Track => tracks.TryGetValue(entry.Id, out var t) ? TrackItem(t, liked.Contains(t.Id)) : null,
                RecentlyPlayedKind.Mix => mixRows.TryGetValue(entry.Id, out var m) ? MixItem(m, mixArt) : null,
                RecentlyPlayedKind.Playlist => playlistRows.TryGetValue(entry.Id, out var p) ? PlaylistItem(p) : null,
                RecentlyPlayedKind.SmartPlaylist => smartRows.TryGetValue(entry.Id, out var s) ? SmartPlaylistItem(s) : null,
                _ => null
            };
            if (item == null) continue;

            item.PlayedAt = entry.PlayedAt;
            items.Add(item);
            if (items.Count == take) break;
        }
        return items;
    }

    // Newest first, one entry per tile. A play counts toward its playlist or
    // mix only when the client said which one; an album play, or one with no
    // source at all, stands for its song.
    internal static List<RecentlyPlayedEntry> Entries(IEnumerable<RecentPlay> plays, int max)
    {
        var seen = new HashSet<(RecentlyPlayedKind, Guid)>();
        var entries = new List<RecentlyPlayedEntry>();
        foreach (var play in plays.OrderByDescending(p => p.PlayedAt))
        {
            var key = TileOf(play);
            if (!seen.Add(key)) continue;

            entries.Add(new RecentlyPlayedEntry(key.Kind, key.Id, play.PlayedAt));
            if (entries.Count >= max) break;
        }
        return entries;
    }

    private static (RecentlyPlayedKind Kind, Guid Id) TileOf(RecentPlay play) => (play.Source, play.SourceId) switch
    {
        (MusicPlaySource.Playlist, Guid id) => (RecentlyPlayedKind.Playlist, id),
        (MusicPlaySource.SmartPlaylist, Guid id) => (RecentlyPlayedKind.SmartPlaylist, id),
        (MusicPlaySource.Mix, Guid id) => (RecentlyPlayedKind.Mix, id),
        _ => (RecentlyPlayedKind.Track, play.TrackId)
    };

    // The same mosaic the AI playlist row draws, so a tile looks the same in
    // both rows.
    private async Task<Dictionary<Guid, AiTrackCandidate>> MixArtAsync(IEnumerable<GeneratedMix> mixRows)
    {
        var trackIds = mixRows
            .Where(m => MixCoverArt.UsesMosaic(m.Kind))
            .SelectMany(m => m.TrackOrder.Take(MixCoverArt.TracksSampled))
            .Distinct()
            .ToList();
        if (trackIds.Count == 0) return new Dictionary<Guid, AiTrackCandidate>();
        return (await aiPlaylists.GetTrackArtAsync(trackIds)).ToDictionary(a => a.TrackId);
    }

    private static RecentlyPlayedItemVM TrackItem(Track track, bool isLiked)
    {
        var vm = MusicManager.ToArtistTrack(track, isLiked);
        return new RecentlyPlayedItemVM
        {
            Kind = RecentlyPlayedKind.Track,
            Id = track.Id,
            Title = track.Title,
            Subtitle = vm.AlbumTitle ?? vm.Artist,
            ArtworkUrl = vm.AlbumArtworkUrl,
            Track = vm
        };
    }

    private static RecentlyPlayedItemVM MixItem(GeneratedMix mix, IReadOnlyDictionary<Guid, AiTrackCandidate> art) => new()
    {
        Kind = RecentlyPlayedKind.Mix,
        Id = mix.Id,
        Title = mix.Name,
        Subtitle = MixCaption(mix.Kind),
        ArtworkUrl = mix.ArtworkUrl,
        ArtworkUrls = MixCoverArt.UsesMosaic(mix.Kind)
            ? MixCoverArt.Pick(mix.TrackOrder.Take(MixCoverArt.TracksSampled).Where(art.ContainsKey).Select(id => art[id]))
            : new List<string>(),
        MixKind = mix.Kind,
        TrackCount = mix.TrackOrder.Count
    };

    // The row each kind sits in on For You, so the tile says where it is from.
    internal static string MixCaption(GeneratedMixKind kind) => kind switch
    {
        GeneratedMixKind.AiPlaylist => "Made for you by AI",
        GeneratedMixKind.Bridge => "Bridge",
        GeneratedMixKind.Blend => "Blend",
        GeneratedMixKind.Requested => "Your request",
        GeneratedMixKind.ReleaseRadar => "Release Radar",
        GeneratedMixKind.MoodMix => "Mood",
        _ => "Made for You"
    };

    private static RecentlyPlayedItemVM PlaylistItem(Playlists.ViewModels.PlaylistSummaryVM playlist) => new()
    {
        Kind = RecentlyPlayedKind.Playlist,
        Id = playlist.Id,
        Title = playlist.Name,
        Subtitle = "Playlist",
        ArtworkUrl = playlist.ImageUrl ?? playlist.PosterUrls.FirstOrDefault(),
        ArtworkUrls = playlist.ImageUrl == null && playlist.PosterUrls.Count > 1 ? playlist.PosterUrls : new List<string>(),
        TrackCount = playlist.ItemCount
    };

    private static RecentlyPlayedItemVM SmartPlaylistItem(SmartPlaylist playlist) => new()
    {
        Kind = RecentlyPlayedKind.SmartPlaylist,
        Id = playlist.Id,
        Title = playlist.Name,
        Subtitle = "Smart playlist",
        ArtworkUrl = playlist.ArtworkUrl
    };
}

internal sealed record RecentlyPlayedEntry(RecentlyPlayedKind Kind, Guid Id, DateTime PlayedAt);

public enum RecentlyPlayedKind
{
    Track = 0,
    Playlist = 1,
    SmartPlaylist = 2,
    Mix = 3
}

public class RecentlyPlayedItemVM
{
    public RecentlyPlayedKind Kind { get; set; }

    // The mix, playlist or smart playlist to open, or the song to play.
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    // Ready to show under the title: where a playlist tile is from, or a song's
    // album.
    public string? Subtitle { get; set; }
    public string? ArtworkUrl { get; set; }

    // Up to four covers for a mosaic, as the playlist and AI playlist tiles
    // draw; empty when ArtworkUrl is the picture.
    public List<string> ArtworkUrls { get; set; } = new();
    public GeneratedMixKind? MixKind { get; set; }
    public int TrackCount { get; set; }
    public DateTime PlayedAt { get; set; }

    // Set for a song, in the shape the other song rows use.
    public ArtistTrackVM? Track { get; set; }
}
