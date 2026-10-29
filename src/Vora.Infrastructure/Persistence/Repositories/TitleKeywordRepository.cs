using Microsoft.EntityFrameworkCore;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class TitleKeywordRepository(VoraDbContext context) : ITitleKeywordRepository
{
    public Task<bool> HasTitlesAwaitingKeywordsAsync(Guid libraryId) => AwaitingKeywords(libraryId).AnyAsync();

    public async Task<List<TitleAwaitingKeywords>> GetTitlesAwaitingKeywordsAsync(Guid libraryId, IReadOnlyCollection<Guid> skipped, int limit)
    {
        var skippedIds = skipped.ToList();
        var rows = await AwaitingKeywords(libraryId)
            .Where(m => !skippedIds.Contains(m.Id))
            .OrderBy(m => m.Id)
            .Take(limit)
            .Select(m => new { m.Id, m.TmdbId, IsShow = m is TvShow })
            .ToListAsync();

        return rows
            .Where(r => !string.IsNullOrEmpty(r.TmdbId))
            .Select(r => new TitleAwaitingKeywords(r.Id, r.TmdbId ?? string.Empty, r.IsShow))
            .ToList();
    }

    public async Task SaveKeywordsAsync(IReadOnlyDictionary<Guid, List<string>> keywords)
    {
        var ids = keywords.Keys.ToList();
        var titles = await context.MediaItems.Where(m => ids.Contains(m.Id)).ToListAsync();
        foreach (var title in titles)
        {
            title.Keywords = keywords[title.Id];
        }
        await context.SaveChangesAsync();
    }

    private IQueryable<MediaItem> AwaitingKeywords(Guid libraryId) =>
        context.MediaItems.AsNoTracking()
            .Where(MediaCapabilities.HasProviderIdentity)
            .Where(m => m.LibraryId == libraryId && m.MissingSince == null && m.Keywords == null && m.TmdbId != null && m.TmdbId != "");
}
