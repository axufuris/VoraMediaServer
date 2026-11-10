using Vora.Application.Ai.Dtos;
using Vora.Application.Ai.ViewModels;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Recommendations;

public interface IOpenAiRecommendationRepository
{
    Task<TasteContext> GetTasteContextAsync(Guid profileId, Guid? libraryId, int recentCount, int lovedCount);
    Task LogAiUsageAsync(AiUsageLog log);
    Task<List<Guid>> VectorSearchUnwatchedMediaAsync(Guid profileId, Guid? libraryId, float[] searchVector, int limit, string embeddingModel);
    Task<List<MediaItemForEmbeddingDto>> GetMediaItemsMissingEmbeddingsAsync(int batchSize, string embeddingModel);
    Task SaveEmbeddingsAsync(List<MediaItemEmbedding> embeddings);
    Task<bool> IsAiEnabledForProfileAsync(Guid profileId);
    Task<AiStatsDashboardVM> GetAiStatsDashboardAsync(DateTime? startDate, DateTime? endDate, int page, int pageSize, string? pluginId);
}

// What the AI is told about a profile: "Title (Year)" for the titles watched
// most recently and the ones rated highest, and what the page lists ("movies",
// "TV shows", or both).
public sealed record TasteContext(IReadOnlyList<string> Recent, IReadOnlyList<string> Loved, string Kind);
