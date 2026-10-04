using Microsoft.EntityFrameworkCore;
using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class ChannelFavoriteRepository(VoraDbContext context) : IChannelFavoriteRepository
{
    public async Task<List<FavoriteChannel>> GetFavoriteChannelsAsync(Guid profileId, IptvChannelKind kind)
    {
        var rows = await context.ProfileChannelFavorites
            .AsNoTracking()
            .Where(f => f.ProfileId == profileId)
            .Join(context.IptvChannels.Where(c => c.Kind == kind),
                f => new { f.PlaylistId, f.ExternalChannelId },
                c => new { c.PlaylistId, c.ExternalChannelId },
                (f, c) => new { Channel = c, c.Playlist, f.AddedAt })
            .ToListAsync();

        return rows.Select(r =>
        {
            r.Channel.Playlist = r.Playlist;
            return new FavoriteChannel(r.Channel, r.AddedAt);
        }).ToList();
    }

    public async Task<List<IptvChannel>> FindChannelsByExternalIdsAsync(IReadOnlyCollection<string> externalChannelIds, IptvChannelKind kind)
    {
        var lowered = externalChannelIds.Select(id => id.ToLowerInvariant()).Distinct().ToList();
        return await context.IptvChannels
            .AsNoTracking()
            .Where(c => c.Kind == kind && lowered.Contains(c.ExternalChannelId.ToLower()))
            .ToListAsync();
    }

    public Task<List<IptvChannel>> FindChannelsByIdsAsync(IReadOnlyCollection<Guid> channelIds, IptvChannelKind kind)
    {
        var ids = channelIds.ToList();
        return context.IptvChannels
            .AsNoTracking()
            .Where(c => c.Kind == kind && ids.Contains(c.Id))
            .ToListAsync();
    }

    public async Task<bool> ReplaceFavoritesAsync(Guid profileId, IptvChannelKind kind, IReadOnlyCollection<ChannelFavoriteKey> favorites)
    {
        try
        {
            return await ApplyReplacementAsync(profileId, kind, favorites);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return await ApplyReplacementAsync(profileId, kind, favorites);
        }
    }

    private async Task<bool> ApplyReplacementAsync(Guid profileId, IptvChannelKind kind, IReadOnlyCollection<ChannelFavoriteKey> favorites)
    {
        if (!await context.UserProfiles.AnyAsync(p => p.Id == profileId))
        {
            return false;
        }

        var existing = await context.ProfileChannelFavorites
            .Where(f => f.ProfileId == profileId)
            .ToListAsync();

        var kindKeys = await context.IptvChannels
            .AsNoTracking()
            .Where(c => c.Kind == kind)
            .Where(c => context.ProfileChannelFavorites.Any(f => f.ProfileId == profileId && f.PlaylistId == c.PlaylistId && f.ExternalChannelId == c.ExternalChannelId))
            .Select(c => new { c.PlaylistId, c.ExternalChannelId })
            .ToListAsync();

        var wanted = favorites.Select(f => (f.PlaylistId, f.ExternalChannelId)).ToHashSet();
        var inScope = kindKeys.Select(k => (k.PlaylistId, k.ExternalChannelId)).ToHashSet();

        var removals = existing
            .Where(f => inScope.Contains((f.PlaylistId, f.ExternalChannelId)) && !wanted.Contains((f.PlaylistId, f.ExternalChannelId)))
            .ToList();

        var present = existing.Select(f => (f.PlaylistId, f.ExternalChannelId)).ToHashSet();
        var playlistIds = wanted.Select(w => w.PlaylistId).Distinct().ToList();
        var livePlaylists = (await context.IptvPlaylists
            .Where(p => playlistIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        var now = DateTime.UtcNow;
        var additions = wanted
            .Where(w => !present.Contains(w) && livePlaylists.Contains(w.PlaylistId))
            .Select(w => new ProfileChannelFavorite { ProfileId = profileId, PlaylistId = w.PlaylistId, ExternalChannelId = w.ExternalChannelId, AddedAt = now })
            .ToList();

        if (removals.Count == 0 && additions.Count == 0)
        {
            return false;
        }

        context.ProfileChannelFavorites.RemoveRange(removals);
        context.ProfileChannelFavorites.AddRange(additions);
        await context.SaveChangesAsync();
        return true;
    }
}
