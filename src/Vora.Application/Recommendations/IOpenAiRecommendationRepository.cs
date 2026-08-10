using Vora.Application.Ai.Dtos;
using Vora.Application.Ai.ViewModels;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Recommendations;

public interface IOpenAiRecommendationRepository
{
    Task<List<string>> GetRecentWatchHistoryContextAsync(Guid profileId, int count);
    Task LogAiUsageAsync(AiUsageLog log);
    Task<List<Guid>> VectorSearchUnwatchedMediaAsync(Guid profileId, Guid? libraryId, float[] searchVector, int limit);
    Task<List<MediaItemForEmbeddingDto>> GetMediaItemsMissingEmbeddingsAsync(int batchSize);
    Task SaveEmbeddingsAsync(List<MediaItemEmbedding> embeddings);
    Task<bool> IsAiEnabledForProfileAsync(Guid profileId);
    Task<AiStatsDashboardVM> GetAiStatsDashboardAsync(DateTime? startDate, DateTime? endDate, int page, int pageSize, string? pluginId);
}
