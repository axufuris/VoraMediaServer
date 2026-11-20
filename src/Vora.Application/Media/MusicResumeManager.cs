using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Media;

public enum MusicResumeSaveOutcome
{
    Saved,
    Cleared,
    Invalid
}

public interface IMusicResumeManager
{
    Task<MusicResumeSaveOutcome> SaveAsync(Guid profileId, MusicPlaySource source, Guid? sourceId, IReadOnlyList<Guid> trackIds, int index);
    Task<List<MusicResumeItemVM>> GetItemsAsync(Guid profileId, PlaylistAccessFilter access, int limit);
    Task<MusicResumeQueueVM?> GetQueueAsync(Guid profileId, MusicAccessFilter access, MusicPlaySource source, Guid? sourceId);
}

public class MusicResumeManager(
    IMusicResumeRepository points,
    IMusicRepository music,
    IRecentlyPlayedManager tiles,
    IClientNotifier notifier,
    ILogger<MusicResumeManager> logger) : IMusicResumeManager
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;

    public async Task<MusicResumeSaveOutcome> SaveAsync(Guid profileId, MusicPlaySource source, Guid? sourceId, IReadOnlyList<Guid> trackIds, int index)
    {
        if (!Enum.IsDefined(source)) return MusicResumeSaveOutcome.Invalid;

        var key = KeyOf(source, sourceId);
        if (source != MusicPlaySource.None && key == Guid.Empty) return MusicResumeSaveOutcome.Invalid;
        if (trackIds.Count > MusicResumeLimits.MaxAccepted) return MusicResumeSaveOutcome.Invalid;

        if (trackIds.Count == 0)
        {
            await points.RemoveAsync(profileId, source, key);
            await NotifyAsync(profileId);
            return MusicResumeSaveOutcome.Cleared;
        }

        if (index < 0 || index >= trackIds.Count) return MusicResumeSaveOutcome.Invalid;

        var (window, windowIndex) = Window(trackIds, index);
        await points.SaveAsync(profileId, source, key, window, windowIndex);
        await NotifyAsync(profileId);
        return MusicResumeSaveOutcome.Saved;
    }

    public async Task<List<MusicResumeItemVM>> GetItemsAsync(Guid profileId, PlaylistAccessFilter access, int limit)
    {
        var take = Math.Clamp(limit, 1, MaxLimit);
        var stored = (await points.GetAsync(profileId, MusicResumeLimits.MaxPoints))
            .Where(p => p.TrackIndex >= 0 && p.TrackIndex < p.TrackIds.Count)
            .ToList();
        if (stored.Count == 0) return new List<MusicResumeItemVM>();

        var built = await tiles.TilesAsync(profileId, access, stored.Select(EntryOf).ToList());
        var currentTracks = (await music.GetTracksByIdsAsync(stored.Select(CurrentTrackId).Distinct().ToList(), access.Music))
            .ToDictionary(t => t.Id);

        var items = new List<MusicResumeItemVM>();
        for (var i = 0; i < stored.Count; i++)
        {
            var tile = built[i];
            var point = stored[i];
            if (tile == null || !currentTracks.TryGetValue(CurrentTrackId(point), out var current)) continue;

            var item = From(tile);
            if (item.Kind != RecentlyPlayedKind.Track) item.Subtitle = current.Title;
            item.PlayedAt = point.UpdatedAt;
            item.Source = point.Source;
            item.SourceId = point.Source == MusicPlaySource.None ? null : point.SourceId;
            item.TrackIndex = point.TrackIndex;
            items.Add(item);
            if (items.Count == take) break;
        }
        return items;
    }

    public async Task<MusicResumeQueueVM?> GetQueueAsync(Guid profileId, MusicAccessFilter access, MusicPlaySource source, Guid? sourceId)
    {
        var key = KeyOf(source, sourceId);
        if (!Enum.IsDefined(source) || (source != MusicPlaySource.None && key == Guid.Empty)) return null;

        var point = await points.GetAsync(profileId, source, key);
        if (point == null) return null;

        var found = (await music.GetTracksByIdsAsync(point.TrackIds.Distinct().ToList(), access)).ToDictionary(t => t.Id);
        if (found.Count == 0) return null;

        var liked = await music.GetLikedTrackIdsAsync(profileId, found.Keys);
        var tracks = new List<ArtistTrackVM>();
        int? startAt = null;
        for (var i = 0; i < point.TrackIds.Count; i++)
        {
            if (!found.TryGetValue(point.TrackIds[i], out var track)) continue;
            if (startAt == null && i >= point.TrackIndex) startAt = tracks.Count;
            tracks.Add(MusicManager.ToArtistTrack(track, liked.Contains(track.Id)));
        }

        return new MusicResumeQueueVM
        {
            Source = source,
            SourceId = source == MusicPlaySource.None ? null : key,
            Index = startAt ?? 0,
            Tracks = tracks
        };
    }

    internal static (List<Guid> Ids, int Index) Window(IReadOnlyList<Guid> ids, int index)
    {
        if (ids.Count <= MusicResumeLimits.MaxQueue) return (ids.ToList(), index);

        var start = Math.Min(Math.Max(0, index - MusicResumeLimits.LookBehind), ids.Count - MusicResumeLimits.MaxQueue);
        return (ids.Skip(start).Take(MusicResumeLimits.MaxQueue).ToList(), index - start);
    }

    private static Guid KeyOf(MusicPlaySource source, Guid? sourceId) =>
        source == MusicPlaySource.None ? Guid.Empty : sourceId.GetValueOrDefault();

    private static Guid CurrentTrackId(MusicResumePoint point) => point.TrackIds[point.TrackIndex];

    private static (RecentlyPlayedKind Kind, Guid Id) EntryOf(MusicResumePoint point) => point.Source switch
    {
        MusicPlaySource.Playlist => (RecentlyPlayedKind.Playlist, point.SourceId),
        MusicPlaySource.SmartPlaylist => (RecentlyPlayedKind.SmartPlaylist, point.SourceId),
        MusicPlaySource.Mix => (RecentlyPlayedKind.Mix, point.SourceId),
        _ => (RecentlyPlayedKind.Track, CurrentTrackId(point))
    };

    private static MusicResumeItemVM From(RecentlyPlayedItemVM tile) => new()
    {
        Kind = tile.Kind,
        Id = tile.Id,
        Title = tile.Title,
        Subtitle = tile.Subtitle,
        ArtworkUrl = tile.ArtworkUrl,
        ArtworkUrls = tile.ArtworkUrls,
        MixKind = tile.MixKind,
        TrackCount = tile.TrackCount,
        PlayedAt = tile.PlayedAt,
        Track = tile.Track
    };

    private async Task NotifyAsync(Guid profileId)
    {
        try
        {
            await notifier.NotifyMusicResumeUpdatedAsync(profileId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not tell profile {Profile}'s devices about a new resume point", profileId);
        }
    }
}
