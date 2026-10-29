using Vora.Domain.Entities.Collections;

namespace Vora.Application.Collections;

public interface ICollectionThemeRepository
{
    Task<CollectionTheme?> GetAsync(Guid collectionId);
    Task SaveAsync(CollectionTheme theme, bool forgetVerdicts);
    Task<Dictionary<Guid, bool>> GetVerdictsAsync(Guid collectionId);
    Task SaveVerdictsAsync(Guid collectionId, IReadOnlyDictionary<Guid, bool> verdicts);
    Task<List<Guid>> FindByKeywordsAsync(IReadOnlyCollection<string> keywords, Guid? libraryId);
    Task<List<Guid>> FindNearestAsync(float[] vector, string embeddingModel, Guid? libraryId, int limit);
    Task<List<ThemeCandidate>> GetCandidatesAsync(IReadOnlyCollection<Guid> mediaItemIds);
}

public sealed record ThemeCandidate(Guid Id, string Title, int? Year, bool IsShow, string? Overview, List<string> Genres);
