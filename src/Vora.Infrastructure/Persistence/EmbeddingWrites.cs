using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Ai;

namespace Vora.Infrastructure.Persistence;

public static class EmbeddingWrites
{
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
