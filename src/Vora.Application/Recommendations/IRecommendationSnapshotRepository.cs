using Vora.Plugins.Dtos;

namespace Vora.Application.Recommendations;

// The For You rows each provider last built for a profile, per library (null:
// Home's For You). For You reads these and never waits on a provider.
public interface IRecommendationSnapshotRepository
{
    // One statement: the stored rows of these providers, and, when asked, when
    // the profile last played or rated anything, which says whether rows built
    // from its history are out of date.
    Task<StoredRecommendations> GetAsync(Guid profileId, Guid? libraryId, IReadOnlyCollection<string> providerIds, bool withLastActivity);

    // Inserts or replaces the provider's rows. Rows built earlier than the ones
    // already stored don't replace them, and nothing is stored for a profile
    // that no longer exists.
    Task SaveAsync(Guid profileId, Guid? libraryId, string providerId, IReadOnlyList<RecommendationListDto> rows, DateTime generatedAt);

    Task DeleteAsync(Guid profileId, Guid? libraryId, string providerId);
}

public sealed record StoredRecommendations(IReadOnlyList<StoredProviderRows> Providers, DateTime? LastActivityAt);

public sealed record StoredProviderRows(string ProviderId, List<RecommendationListDto> Rows, DateTime GeneratedAt);
