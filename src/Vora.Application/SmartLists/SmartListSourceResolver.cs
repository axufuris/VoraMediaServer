using Microsoft.Extensions.Logging;
using Vora.Application.Iptv;
using Vora.Application.Iptv.ViewModels;
using Vora.Application.Media;
using Vora.Application.Podcasts;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.SmartLists;

public interface ISmartListSourceResolver
{
    Task<List<SmartListEntryVM>> ResolveAsync(SmartList list, SmartListRulesDto? rules, SmartListViewer viewer);
}

public class SmartListSourceResolver(
    IChannelFavoriteRepository favoriteRepository,
    IUserManager userManager,
    IIptvEpgService epgService,
    IPodcastManager podcastManager,
    IMusicManager musicManager,
    IIptvRepository iptvRepository,
    ILogger<SmartListSourceResolver> logger) : ISmartListSourceResolver
{
    private const int MaxPodcastFetch = 200;

    public Task<List<SmartListEntryVM>> ResolveAsync(SmartList list, SmartListRulesDto? rules, SmartListViewer viewer) => list.Source switch
    {
        SmartListSource.FavoriteChannels => ResolveFavoritesAsync(list, viewer, IptvChannelKind.Tv),
        SmartListSource.FavoriteStations => ResolveFavoritesAsync(list, viewer, IptvChannelKind.Radio),
        SmartListSource.NewPodcastEpisodes => ResolvePodcastEpisodesAsync(list, rules, viewer),
        SmartListSource.RecentlyAddedMusic => ResolveAlbumsAsync(list, viewer),
        SmartListSource.RecentRecordings => ResolveRecordingsAsync(list, rules, viewer),
        _ => Task.FromResult(new List<SmartListEntryVM>())
    };

    private async Task<List<SmartListEntryVM>> ResolveFavoritesAsync(SmartList list, SmartListViewer viewer, IptvChannelKind kind)
    {
        if (viewer.ProfileId is not { } profileId)
        {
            return [];
        }

        var favorites = await favoriteRepository.GetFavoriteChannelsAsync(profileId, kind);
        if (favorites.Count == 0)
        {
            return [];
        }

        var canSeePlaylist = await PlaylistAccessAsync(viewer);
        var visible = favorites
            .Where(f => IsWatchable(f, canSeePlaylist))
            .GroupBy(f => f.Channel.ExternalChannelId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.AddedAt).First());

        var chosen = OrderFavorites(visible, list.SortBy).Take(list.MaxItems).ToList();
        var nowPlaying = kind == IptvChannelKind.Tv
            ? await NowPlayingAsync(viewer, chosen.Select(f => f.Channel.ExternalChannelId).ToList())
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return chosen.Select(f => new SmartListEntryVM
        {
            Kind = kind == IptvChannelKind.Tv ? SmartListEntryKind.Channel : SmartListEntryKind.Station,
            Id = f.Channel.Id,
            Title = f.Channel.Name,
            Subtitle = nowPlaying.TryGetValue(f.Channel.ExternalChannelId, out var program) ? program : f.Channel.GroupTitle,
            ImageUrl = f.Channel.LogoUrl,
            Channel = IptvChannelVM.FromEntity(f.Channel, f.Channel.Playlist.Name)
        }).ToList();
    }

    private static bool IsWatchable(FavoriteChannel favorite, Func<Guid, bool> canSeePlaylist)
    {
        var channel = favorite.Channel;
        var playlist = channel.Playlist;
        return playlist.IsActive
            && !channel.IsHiddenByAdmin
            && channel.IsHealthy != false
            && (string.IsNullOrWhiteSpace(playlist.CountryFilter) || string.Equals(channel.CountryCode, playlist.CountryFilter, StringComparison.OrdinalIgnoreCase))
            && canSeePlaylist(channel.PlaylistId);
    }

    private static IEnumerable<FavoriteChannel> OrderFavorites(IEnumerable<FavoriteChannel> favorites, SmartListSortBy sortBy) => sortBy switch
    {
        SmartListSortBy.DateAddedDesc => favorites.OrderByDescending(f => f.AddedAt),
        SmartListSortBy.Random => favorites.OrderBy(_ => Random.Shared.Next()),
        _ => favorites.OrderBy(f => f.Channel.Name, StringComparer.OrdinalIgnoreCase)
    };

    private async Task<Func<Guid, bool>> PlaylistAccessAsync(SmartListViewer viewer)
    {
        if (viewer.AccountId is not { } accountId)
        {
            return _ => false;
        }

        var user = await userManager.GetUserAccountAsync(accountId);
        if (user == null)
        {
            return _ => false;
        }

        var profile = viewer.ProfileId is { } profileId ? user.Profiles.FirstOrDefault(p => p.Id == profileId) : null;
        return playlistId =>
            (user.IsAdmin || user.HasAllIptvAccess || user.AllowedIptvPlaylistIds.Contains(playlistId))
            && (profile == null || profile.IsAdmin || profile.HasAllIptvAccess || profile.AllowedIptvPlaylistIds.Contains(playlistId));
    }

    private async Task<Dictionary<string, string>> NowPlayingAsync(SmartListViewer viewer, List<string> externalChannelIds)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (viewer.AccountId is not { } accountId || viewer.ProfileId is not { } profileId || externalChannelIds.Count == 0)
        {
            return titles;
        }

        var now = DateTime.UtcNow;
        try
        {
            var guide = await epgService.GetFilteredGuideAsync(accountId, profileId, externalChannelIds, now, now.AddMinutes(1));
            foreach (var (channelId, programs) in guide)
            {
                var current = programs.FirstOrDefault(p => p.StartTime <= now && p.EndTime > now);
                if (current != null)
                {
                    titles[channelId] = current.Title;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read what is on now for the favorite channels row.");
        }

        return titles;
    }

    private async Task<List<SmartListEntryVM>> ResolvePodcastEpisodesAsync(SmartList list, SmartListRulesDto? rules, SmartListViewer viewer)
    {
        if (viewer.ProfileId is not { } profileId)
        {
            return [];
        }

        var unplayedOnly = rules?.UnwatchedOnly == true;
        var days = rules?.Days is > 0 ? rules.Days : null;
        var fetch = unplayedOnly ? Math.Min(list.MaxItems * 4, MaxPodcastFetch) : list.MaxItems;

        var episodes = await podcastManager.GetRecentEpisodesAsync(profileId, fetch, days);
        return episodes
            .Where(e => !unplayedOnly || !e.IsPlayed)
            .Take(list.MaxItems)
            .Select(e => new SmartListEntryVM
            {
                Kind = SmartListEntryKind.PodcastEpisode,
                Id = e.Id,
                Title = e.Title,
                Subtitle = e.ShowTitle,
                ImageUrl = e.ArtworkUrl ?? e.ShowArtworkUrl,
                PodcastEpisode = e
            })
            .ToList();
    }

    private async Task<List<SmartListEntryVM>> ResolveAlbumsAsync(SmartList list, SmartListViewer viewer)
    {
        var sort = list.SortBy switch
        {
            SmartListSortBy.ReleaseDateDesc => AlbumSortOrder.Newest,
            SmartListSortBy.TopRated => AlbumSortOrder.Popular,
            SmartListSortBy.TitleAsc => AlbumSortOrder.Alphabetical,
            _ => AlbumSortOrder.RecentlyAdded
        };

        var page = await musicManager.GetAlbumsAsync(list.LibraryId, viewer.MusicAccess, sort, 0, list.MaxItems);
        return page.Items.Select(a => new SmartListEntryVM
        {
            Kind = SmartListEntryKind.Album,
            Id = a.Id,
            Title = a.Title,
            Subtitle = a.AlbumArtist ?? a.ArtistName,
            ImageUrl = a.ArtworkUrl,
            Album = a
        }).ToList();
    }

    private async Task<List<SmartListEntryVM>> ResolveRecordingsAsync(SmartList list, SmartListRulesDto? rules, SmartListViewer viewer)
    {
        if (viewer.ProfileId is not { } profileId)
        {
            return [];
        }

        DateTime? since = rules?.Days is > 0 ? DateTime.UtcNow.AddDays(-rules.Days.Value) : null;
        var sessions = await iptvRepository.GetSessionsForProfileAsync(profileId);

        return sessions
            .Where(s => s.Status == IptvRecordingSessionStatus.Completed && !string.IsNullOrWhiteSpace(s.OutputFilePath))
            .Where(s => since == null || s.StartTime >= since)
            .OrderByDescending(s => s.StartTime)
            .Take(list.MaxItems)
            .Select(s =>
            {
                var recording = IptvRecordingSessionVM.FromEntity(s);
                return new SmartListEntryVM
                {
                    Kind = SmartListEntryKind.Recording,
                    Id = s.Id,
                    Title = s.Title,
                    Subtitle = string.IsNullOrWhiteSpace(s.EpisodeTitle) ? recording.Schedule.Channel.Name : s.EpisodeTitle,
                    ImageUrl = recording.Schedule.Channel.LogoUrl,
                    Recording = recording
                };
            })
            .ToList();
    }
}
