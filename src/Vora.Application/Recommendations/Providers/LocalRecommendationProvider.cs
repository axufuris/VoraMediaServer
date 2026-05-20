using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations.Providers;

public class LocalRecommendationProvider(IRecommendationRepository repository) : IRecommendationProvider
{
    private const int TopGenreCount = 3;
    private const int RecommendationsPerGenre = 15;
    private const int InitialWeight = 100;

    public string Id => "local_recommendations";
    public string Name => "Vora Local Recommendations";
    public string Description => "Analyzes your watch history to recommend highly-rated unwatched content from your library based on your top genres.";
    public string Version => "1.0.0";
    public string Type => "Recommendation";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() =>
        new List<PluginSettingDefinitionDto>();

    public async Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId)
    {
        var lists = new List<RecommendationListDto>();

        var playedMediaIds = await repository.GetPlayedMediaIdsAsync(profileId);
        if (playedMediaIds.Count == 0)
        {
            return lists;
        }

        var topGenres = await repository.GetTopGenresAsync(playedMediaIds, TopGenreCount);
        var weight = InitialWeight;

        foreach (var genre in topGenres)
        {
            var recommendedIds = await repository.GetTopUnwatchedMediaByGenreAsync(genre.Id, profileId, libraryId, RecommendationsPerGenre);
            if (recommendedIds.Count == 0)
            {
                continue;
            }

            lists.Add(new RecommendationListDto
            {
                Title = $"Because you like {genre.Name}",
                Description = $"Top rated unwatched {genre.Name}s in your library.",
                Weight = weight--,
                LocalItemIds = recommendedIds
            });
        }

        return lists;
    }
}
