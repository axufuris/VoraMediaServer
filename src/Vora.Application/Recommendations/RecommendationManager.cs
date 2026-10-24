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
    private readonly IUserMediaStateRepository _userMediaStates;
    private readonly IRecommendationRepository _recommendationRepo;

    public RecommendationManager(IEnumerable<IRecommendationProvider> providers, ISystemSettingsRepository settingsRepo, IUserMediaStateRepository userMediaStates, IRecommendationRepository recommendationRepo)
    {
        _providers = providers;
        _settingsRepo = settingsRepo;
        _userMediaStates = userMediaStates;
        _recommendationRepo = recommendationRepo;
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
                var providerLists = await provider.GetRecommendationsAsync(profileId, libraryId);

                foreach (var list in providerLists)
                {
                    var matchedItems = await _recommendationRepo.GetHydratedMediaItemsAsync(
                        list.LocalItemIds, list.ExternalTmdbIds, libraryId, hasAllAccess, allowedLibs, allowedMovieRatings, allowedTvRatings, blockUnrated);

                    if (matchedItems.Any())
                    {
                        await _userMediaStates.AttachLibraryItemUserStatesAsync(matchedItems, profileId);

                        allLists.Add(new RecommendationListVM
                        {
                            Title = list.Title,
                            Description = list.Description,
                            Weight = list.Weight,
                            Items = matchedItems.OrderBy(m => list.LocalItemIds.IndexOf(m.Id)).ToList()
                        });
                    }
                }
            }
            catch
            {
            }
        }

        return allLists.OrderByDescending(l => l.Weight).ToList();
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