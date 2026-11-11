using Microsoft.Extensions.Logging;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Recommendations.ViewModels;
using Vora.Application.Settings;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations;

public interface IRecommendationManager
{
    Task<List<RecommendationListVM>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, bool hasAllAccess, List<Guid> allowedLibs, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated, string? targetProviderId = null);
    Task<List<string>> GetActiveProviderIdsAsync();
}

public class RecommendationManager : IRecommendationManager
{
    private readonly IEnumerable<IRecommendationProvider> _providers;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly IRecommendationRepository _recommendationRepo;
    private readonly ILogger<RecommendationManager> _logger;

    public RecommendationManager(IEnumerable<IRecommendationProvider> providers, ISystemSettingsRepository settingsRepo, IRecommendationRepository recommendationRepo, ILogger<RecommendationManager> logger)
    {
        _providers = providers;
        _settingsRepo = settingsRepo;
        _recommendationRepo = recommendationRepo;
        _logger = logger;
    }

    public async Task<List<RecommendationListVM>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, bool hasAllAccess, List<Guid> allowedLibs, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated, string? targetProviderId = null)
    {
        var activeProviders = new List<IRecommendationProvider>();

        foreach (var provider in _providers)
        {
            if (targetProviderId != null && provider.Id != targetProviderId) continue;

            var isEnabledStr = await _settingsRepo.GetPluginSettingAsync(provider.Id, "is_enabled");
            if (isEnabledStr != "false") activeProviders.Add(provider);
        }

        var allLists = new List<RecommendationListVM>();

        foreach (var provider in activeProviders)
        {
            try
            {
                var providerLists = (await provider.GetRecommendationsAsync(profileId, libraryId)).ToList();

                // Every row of library titles is loaded in one query: a row at a
                // time made For You fifteen tile queries. A row named by TMDB ids
                // (an AI provider's) is matched on its own, since a tile doesn't
                // carry the id it matched by.
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
            }
            catch (Exception ex)
            {
                // One provider failing (the AI's token limit, a timeout) leaves the
                // others' rows, but it is said, not swallowed.
                _logger.LogWarning(ex, "For You provider {Provider} failed for profile {Profile}.", provider.Id, profileId);
            }
        }

        // A list's weight is its place on the page, as the clients sort it.
        return allLists.OrderBy(l => l.Weight).ToList();
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