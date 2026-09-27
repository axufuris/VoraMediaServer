using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IRecommendationProvider : IVoraPlugin
{
    Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, CancellationToken cancellationToken = default);
}
