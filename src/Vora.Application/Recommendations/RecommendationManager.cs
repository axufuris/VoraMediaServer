using Microsoft.Extensions.Logging;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations;

public interface IRecommendationManager
{
    Task<List<RecommendationListVM>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, bool hasAllAccess, List<Guid> allowedLibs, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated, string? targetProviderId = null);
    Task<List<string>> GetActiveProviderIdsAsync();

    // Asks the provider for the profile's rows and stores them. ForYouRebuildWorker
    // runs this, never a page request.
    Task<ForYouRebuildResult> RebuildAsync(ForYouRebuild rebuild, CancellationToken cancellationToken);
}

public enum ForYouRebuildResult
{
    Stored,

    // An outside-call provider found nothing. That isn't kept, so the next look
    // tries again rather than showing no AI rows for twelve hours.
    NothingKept,

    // The provider is no longer installed.
    NoProvider
}

// For You is a read of the rows each provider last built (RecommendationSnapshots,
// docs/for-you.md). A page request never waits on an AI call: out-of-date rows
// are shown as they are and a rebuild is queued behind them. The one provider
// call made while the page waits is the first build of a provider that makes
// no outside call, so a first visit isn't empty.
public class RecommendationManager : IRecommendationManager
{
    private readonly IEnumerable<IRecommendationProvider> _providers;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IRecommendationRepository _recommendationRepo;
    private readonly IRecommendationSnapshotRepository _snapshots;
    private readonly IForYouRebuildQueue _rebuilds;
    private readonly TimeProvider _time;
    private readonly ILogger<RecommendationManager> _logger;

    public RecommendationManager(
        IEnumerable<IRecommendationProvider> providers,
        ISystemSettingsRepository settingsRepo,
        IRecommendationRepository recommendationRepo,
        IRecommendationSnapshotRepository snapshots,
        IForYouRebuildQueue rebuilds,
        TimeProvider time,
        ILogger<RecommendationManager> logger)
    {
        _providers = providers;
        _settingsRepo = settingsRepo;
        _recommendationRepo = recommendationRepo;
        _snapshots = snapshots;
        _rebuilds = rebuilds;
        _time = time;
        _logger = logger;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<List<RecommendationListVM>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, bool hasAllAccess, List<Guid> allowedLibs, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated, string? targetProviderId = null)
    {
        var activeProviders = new List<IRecommendationProvider>();

        foreach (var provider in _providers)
        {
            if (targetProviderId != null && provider.Id != targetProviderId) continue;

            var isEnabledStr = await _settingsRepo.GetPluginSettingAsync(provider.Id, "is_enabled");
            if (isEnabledStr != "false") activeProviders.Add(provider);
        }

        if (activeProviders.Count == 0) return new List<RecommendationListVM>();

        // The last activity is only read when a provider's rows are built from
        // the history alone, and comes in the same statement as the rows.
        var stored = await _snapshots.GetAsync(
            profileId, libraryId, activeProviders.Select(p => p.Id).ToList(), withLastActivity: activeProviders.Any(p => !p.MakesOutsideCalls));
        var now = Now;

        var providerLists = new List<RecommendationListDto>();
        foreach (var provider in activeProviders)
        {
            var snapshot = stored.Providers.FirstOrDefault(s => s.ProviderId == provider.Id);
            if (snapshot != null)
            {
                if (RecommendationFreshness.IsStale(provider.MakesOutsideCalls, snapshot.GeneratedAt, stored.LastActivityAt, now))
                {
                    _rebuilds.TryQueue(new ForYouRebuild(profileId, libraryId, provider.Id));
                }
                providerLists.AddRange(snapshot.Rows);
            }
            else if (!provider.MakesOutsideCalls)
            {
                providerLists.AddRange(await BuildWhileWaitingAsync(provider, profileId, libraryId));
            }
            else
            {
                // Built in the background; its rows show on the next visit.
                _rebuilds.TryQueue(new ForYouRebuild(profileId, libraryId, provider.Id));
            }
        }

        // Every row of library titles is loaded in one query: a row at a time
        // made For You fifteen tile queries. A row named by TMDB ids is matched
        // on its own, since a tile doesn't carry the id it matched by. Access
        // and watch state are the viewer's now, whenever the rows were built.
        var localIds = providerLists
            .Where(l => l.ExternalTmdbIds.Count == 0)
            .SelectMany(l => l.LocalItemIds)
            .Distinct()
            .ToList();
        var local = localIds.Count == 0
            ? new Dictionary<Guid, LibraryItemVM>()
            : (await _recommendationRepo.GetHydratedMediaItemsAsync(
                profileId, localIds, new List<string>(), libraryId, hasAllAccess, allowedLibs, allowedMovieRatings, allowedTvRatings, blockUnrated))
                .ToDictionary(m => m.Id);

        var allLists = new List<RecommendationListVM>();
        foreach (var list in providerLists)
        {
            var matchedItems = list.ExternalTmdbIds.Count == 0
                ? list.LocalItemIds.Distinct().Where(local.ContainsKey).Select(id => local[id]).ToList()
                : (await _recommendationRepo.GetHydratedMediaItemsAsync(
                    profileId, list.LocalItemIds, list.ExternalTmdbIds, libraryId, hasAllAccess, allowedLibs, allowedMovieRatings, allowedTvRatings, blockUnrated))
                    .OrderBy(m => list.LocalItemIds.IndexOf(m.Id))
                    .ToList();

            if (matchedItems.Any())
            {
                allLists.Add(new RecommendationListVM
                {
                    Title = list.Title,
                    Description = list.Description,
                    Weight = list.Weight,
                    Items = matchedItems
                });
            }
        }

        // A list's weight is its place on the page, as the clients sort it.
        return allLists.OrderBy(l => l.Weight).ToList();
    }

    // A provider with no outside call and no rows stored yet is built once
    // while the page waits, so a first visit isn't empty. What it finds is
    // stored, nothing included: a profile that has watched nothing yet then
    // reads an empty list until it plays something.
    private async Task<List<RecommendationListDto>> BuildWhileWaitingAsync(IRecommendationProvider provider, Guid profileId, Guid? libraryId)
    {
        var startedAt = Now;
        List<RecommendationListDto> rows;
        try
        {
            rows = (await provider.GetRecommendationsAsync(profileId, libraryId)).ToList();
        }
        catch (Exception ex)
        {
            // One provider failing leaves the others' rows, but it is said, not
            // swallowed.
            _logger.LogWarning(ex, "For You provider {Provider} failed for profile {Profile}.", provider.Id, profileId);
            return new List<RecommendationListDto>();
        }

        try
        {
            await _snapshots.SaveAsync(profileId, libraryId, provider.Id, rows, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not store the For You rows of {Provider} for profile {Profile}; they are built again next time.", provider.Id, profileId);
        }

        return rows;
    }

    public async Task<ForYouRebuildResult> RebuildAsync(ForYouRebuild rebuild, CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == rebuild.ProviderId);
        if (provider == null) return ForYouRebuildResult.NoProvider;

        // Built from what was there when it started: a play recorded while it
        // runs makes the rows out of date again rather than being missed.
        var startedAt = Now;
        var rows = (await provider.GetRecommendationsAsync(rebuild.ProfileId, rebuild.LibraryId, cancellationToken)).ToList();

        if (rows.Count == 0 && provider.MakesOutsideCalls)
        {
            // Nothing found is not kept, so the next look tries again rather
            // than showing no AI rows for twelve hours. The rows from before go
            // too, as they did when the cache's twelve hours ran out: an empty
            // answer is also what a profile whose account turned AI
            // recommendations off gets.
            await _snapshots.DeleteAsync(rebuild.ProfileId, rebuild.LibraryId, provider.Id);
            return ForYouRebuildResult.NothingKept;
        }

        await _snapshots.SaveAsync(rebuild.ProfileId, rebuild.LibraryId, provider.Id, rows, startedAt);
        return ForYouRebuildResult.Stored;
    }

    public async Task<List<string>> GetActiveProviderIdsAsync()
    {
        var active = new List<string>();
        foreach (var provider in _providers)
        {
            var isEnabledStr = await _settingsRepo.GetPluginSettingAsync(provider.Id, "is_enabled");
            if (isEnabledStr != "false") active.Add(provider.Id);
        }
        return active;
    }
}
