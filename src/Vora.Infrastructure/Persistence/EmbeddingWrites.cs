using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Ai;

namespace Vora.Infrastructure.Persistence;

public static class EmbeddingWrites
{
    public static async Task<int> UpsertAsync(VoraDbContext context, IReadOnlyList<MediaItemEmbedding> embeddings)
    {
        var latest = embeddings.GroupBy(e => e.MediaItemId).Select(g => g.Last()).ToList();
        if (latest.Count == 0) return 0;

        for (var attempt = 0; ; attempt++)
        {
            var ids = latest.Select(e => e.MediaItemId).ToList();
            var living = (await context.MediaItems.AsNoTracking().Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync()).ToHashSet();
            var existing = await context.MediaItemEmbeddings.Where(e => ids.Contains(e.MediaItemId)).ToDictionaryAsync(e => e.MediaItemId);

            var written = 0;
            foreach (var incoming in latest.Where(e => living.Contains(e.MediaItemId)))
            {
                if (existing.TryGetValue(incoming.MediaItemId, out var row))
                {
                    row.Embedding = incoming.Embedding;
                    row.SourceHash = incoming.SourceHash;
                    row.Model = incoming.Model;
                    row.LastUpdatedAt = incoming.LastUpdatedAt;
                }
                else
                {
                    context.MediaItemEmbeddings.Add(incoming);
                }
                written++;
            }
            if (written == 0) return 0;

            try
            {
                await context.SaveChangesAsync();
                return written;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                context.ChangeTracker.Clear();
            }
        }
    }

    public static async Task<int> InsertNewAsync(VoraDbContext context, IReadOnlyList<MediaItemEmbedding> embeddings)
    {
        for (var attempt = 0; ; attempt++)
        {
            var ids = embeddings.Select(e => e.MediaItemId).Distinct().ToList();
            var writable = (await context.MediaItems
                    .AsNoTracking()
                    .Where(m => ids.Contains(m.Id) && !context.MediaItemEmbeddings.Any(e => e.MediaItemId == m.Id))
                    .Select(m => m.Id)
                    .ToListAsync())
                .ToHashSet();

            var toInsert = embeddings
                .Where(e => writable.Contains(e.MediaItemId))
                .GroupBy(e => e.MediaItemId)
                .Select(g => g.First())
                .ToList();
            if (toInsert.Count == 0) return 0;

            context.MediaItemEmbeddings.AddRange(toInsert);
            try
            {
                await context.SaveChangesAsync();
                return toInsert.Count;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                foreach (var embedding in toInsert) context.Entry(embedding).State = EntityState.Detached;
            }
        }
    }
}
