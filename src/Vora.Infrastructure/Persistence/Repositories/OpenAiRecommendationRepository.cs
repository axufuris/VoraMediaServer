using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Vora.Application.Recommendations;
using Vora.Application.Ai.Dtos;
using Vora.Application.Ai.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Ai;

namespace Vora.Infrastructure.Persistence.Repositories;

public class OpenAiRecommendationRepository(VoraDbContext context) : IOpenAiRecommendationRepository
{
    private const double CosineDistanceThreshold = 0.80;

    public Task<List<string>> GetRecentWatchHistoryContextAsync(Guid profileId, int count) =>
        context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsPlayed)
            .OrderByDescending(s => s.LastPlayedAt)
            .Take(count)
            .Select(s => $"{s.MediaItem.Title} ({(s.MediaItem.ReleaseDate.HasValue ? s.MediaItem.ReleaseDate.Value.Year.ToString() : "Unknown")})")
            .ToListAsync();

    public async Task LogAiUsageAsync(AiUsageLog log)
    {
        await context.AiUsageLogs.AddAsync(log);
        await context.SaveChangesAsync();
    }

    public Task<List<Guid>> VectorSearchUnwatchedMediaAsync(Guid profileId, Guid? libraryId, float[] searchVector, int limit)
    {
        var pgVector = new Pgvector.Vector(searchVector);

        var query = context.Set<MediaItemEmbedding>()
            .AsNoTracking()
            .Where(e => e.Embedding != null && (e.MediaItem is Movie || e.MediaItem is TvShow));

        if (libraryId.HasValue)
        {
            query = query.Where(e => e.MediaItem.LibraryId == libraryId.Value);
        }

        query = query.Where(e =>
            !context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.MediaItemId && s.IsPlayed));

        return query
            .Where(e => e.Embedding!.CosineDistance(pgVector) < CosineDistanceThreshold)
            .OrderBy(e => e.Embedding!.CosineDistance(pgVector))
            .Take(limit)
            .Select(e => e.MediaItemId)
            .ToListAsync();
    }

    public Task<List<MediaItemForEmbeddingDto>> GetMediaItemsMissingEmbeddingsAsync(int batchSize) =>
        context.MediaItems
            .AsNoTracking()
            .Where(m => (m is Movie || m is TvShow)
                && !context.MediaItemEmbeddings.Any(e => e.MediaItemId == m.Id))
            .Take(batchSize)
            .Select(MediaItemForEmbeddingDto.Projection)
            .ToListAsync();

    public async Task SaveEmbeddingsAsync(List<MediaItemEmbedding> embeddings)
    {
        await context.MediaItemEmbeddings.AddRangeAsync(embeddings);
        await context.SaveChangesAsync();
    }

    public Task<bool> IsAiEnabledForProfileAsync(Guid profileId) =>
        context.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => p.User.EnableAiRecommendations)
            .FirstOrDefaultAsync();

    public async Task<AiStatsDashboardVM> GetAiStatsDashboardAsync(DateTime? startDate, DateTime? endDate, int page, int pageSize, string? pluginId)
    {
        var query = context.AiUsageLogs.AsNoTracking().AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(l => l.Timestamp >= startDate.Value.ToUniversalTime());
        }

        if (endDate.HasValue)
        {
            query = query.Where(l => l.Timestamp < endDate.Value.AddDays(1).ToUniversalTime());
        }

        if (!string.IsNullOrEmpty(pluginId))
        {
            query = query.Where(l => l.PluginId == pluginId);
        }

        var dailyStats = await query
            .GroupBy(l => new { l.Timestamp.Date, l.ModelUsed })
            .Select(g => new DailyAiStatVM
            {
                Date = g.Key.Date,
                ModelUsed = g.Key.ModelUsed,
                PromptTokens = g.Sum(l => l.PromptTokens),
                CompletionTokens = g.Sum(l => l.CompletionTokens)
            })
            .OrderBy(d => d.Date)
            .ToListAsync();

        var totalCount = await query.CountAsync();
        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AiUsageLogVM.Projection)
            .ToListAsync();

        return new AiStatsDashboardVM
        {
            DailyStats = dailyStats,
            Logs = logs,
            TotalLogs = totalCount
        };
    }
}
