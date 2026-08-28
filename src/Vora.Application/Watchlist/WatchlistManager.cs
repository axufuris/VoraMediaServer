using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Watchlist.ViewModels;
using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Watchlist;

public class WatchlistRequest
{
    public Guid? MediaItemId { get; set; }
    public string? ExternalId { get; set; }
    public string? ProviderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public DateTime? ExpectedReleaseDate { get; set; }
}

public interface IWatchlistManager
{
    Task<List<WatchlistItemVM>> GetWatchlistAsync(Guid profileId);
    Task<bool> ToggleAsync(Guid profileId, WatchlistRequest request);
    Task<bool> IsInWatchlistAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId);
}

// The watchlist spans both library items and titles that aren't in the library,
// so it is deliberately not part of Discovery and is not gated behind the
// Discover feature.
//
// An entry is keyed by its external provider identity whenever one is known.
// That is what makes the two halves the same list: bookmark a film from
// Discovery, acquire it later, and the library item resolves to the same row
// rather than a second one. A library item with no external match is keyed by
// its own id instead.
public class WatchlistManager(
    IWatchlistRepository repository,
    IMediaRepository mediaRepository,
    ILogger<WatchlistManager> logger) : IWatchlistManager
{
    private const string TmdbProviderId = "tmdb_discovery";

    public async Task<List<WatchlistItemVM>> GetWatchlistAsync(Guid profileId)
    {
        var items = await repository.GetWatchlistAsync(profileId);

        // Rows added before a title was in the library carry no MediaItemId.
        // Resolve them on read so the client can link locally without needing a
        // backfill pass.
        var unresolved = items.Where(i => i.MediaItemId == null && !string.IsNullOrWhiteSpace(i.ExternalId)).ToList();
        var resolved = new Dictionary<Guid, Guid>();

        foreach (var group in unresolved.GroupBy(i => i.Type))
        {
            var externalIds = group.Select(i => i.ExternalId).Distinct().ToList();
            var localIds = await mediaRepository.GetLocalIdsByExternalIdsAsync(externalIds, group.Key);
            foreach (var item in group)
            {
                if (localIds.TryGetValue(item.ExternalId, out var localId)) resolved[item.Id] = localId;
            }
        }

        return items.Select(i => new WatchlistItemVM
        {
            Id = i.Id,
            ProfileId = i.ProfileId,
            ExternalId = i.ExternalId,
            ProviderId = i.ProviderId,
            Type = i.Type,
            Title = i.Title,
            PosterUrl = i.PosterUrl,
            AddedAt = i.AddedAt,
            MediaItemId = i.MediaItemId ?? (resolved.TryGetValue(i.Id, out var localId) ? localId : null),
        }).ToList();
    }

    public async Task<bool> ToggleAsync(Guid profileId, WatchlistRequest request)
    {
        try
        {
            var key = await ResolveKeyAsync(request);

            var existing = await repository.FindAsync(profileId, key.ExternalId, key.ProviderId, key.MediaItemId);
            if (existing != null)
            {
                await repository.RemoveAsync(existing);
                return false;
            }

            await repository.AddAsync(new UserWatchlistItem
            {
                ProfileId = profileId,
                ExternalId = key.ExternalId ?? string.Empty,
                ProviderId = key.ProviderId ?? string.Empty,
                MediaItemId = key.MediaItemId,
                Type = request.Type,
                Title = request.Title,
                PosterUrl = request.PosterUrl,
                ExpectedReleaseDate = request.ExpectedReleaseDate,
            });
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle watchlist entry for profile {ProfileId}.", profileId);
            throw;
        }
    }

    public async Task<bool> IsInWatchlistAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId)
    {
        var key = await ResolveKeyAsync(new WatchlistRequest
        {
            ExternalId = externalId,
            ProviderId = providerId,
            MediaItemId = mediaItemId,
        });

        return await repository.FindAsync(profileId, key.ExternalId, key.ProviderId, key.MediaItemId) != null;
    }

    // Fills in whichever half of the identity the caller didn't supply: a
    // library item contributes its TMDB id so it dedupes with a Discovery entry,
    // and a Discovery entry picks up the local id when a copy already exists.
    private async Task<(string? ExternalId, string? ProviderId, Guid? MediaItemId)> ResolveKeyAsync(WatchlistRequest request)
    {
        if (request.MediaItemId.HasValue)
        {
            var tmdbId = await mediaRepository.GetTmdbIdAsync(request.MediaItemId.Value);
            return string.IsNullOrWhiteSpace(tmdbId)
                ? (null, null, request.MediaItemId)
                : (tmdbId, TmdbProviderId, request.MediaItemId);
        }

        if (string.IsNullOrWhiteSpace(request.ExternalId)) return (null, null, null);

        var localIds = await mediaRepository.GetLocalIdsByExternalIdsAsync([request.ExternalId], request.Type);
        var localId = localIds.TryGetValue(request.ExternalId, out var found) ? found : (Guid?)null;
        return (request.ExternalId, request.ProviderId, localId);
    }
}
