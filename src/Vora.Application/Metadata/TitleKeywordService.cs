using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Metadata;

public interface ITitleKeywordRepository
{
    Task<bool> HasTitlesAwaitingKeywordsAsync(Guid libraryId);
    Task<List<TitleAwaitingKeywords>> GetTitlesAwaitingKeywordsAsync(Guid libraryId, IReadOnlyCollection<Guid> skipped, int limit);
    Task SaveKeywordsAsync(IReadOnlyDictionary<Guid, List<string>> keywords);
}

public sealed record TitleAwaitingKeywords(Guid Id, string TmdbId, bool IsShow);

public interface ITitleKeywordService
{
    Task<bool> HasAwaitingAsync(Guid libraryId);
    Task<int> FillAwaitingAsync(Guid libraryId, CancellationToken cancellationToken);
}

public class TitleKeywordService(ITitleKeywordRepository repository, IEnumerable<IMetadataProvider> providers, ILogger<TitleKeywordService> logger) : ITitleKeywordService
{
    public const int PageSize = 100;
    private const int Parallelism = 4;

    public Task<bool> HasAwaitingAsync(Guid libraryId) => repository.HasTitlesAwaitingKeywordsAsync(libraryId);

    public async Task<int> FillAwaitingAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var filled = 0;
        var skipped = new HashSet<Guid>();

        while (true)
        {
            var page = await repository.GetTitlesAwaitingKeywordsAsync(libraryId, skipped, PageSize);
            if (page.Count == 0) return filled;

            var found = new ConcurrentDictionary<Guid, List<string>>();
            await Parallel.ForEachAsync(page, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken }, async (title, ct) =>
            {
                if (await FetchAsync(title, ct) is { } keywords) found[title.Id] = keywords;
            });

            skipped.UnionWith(page.Select(title => title.Id).Where(id => !found.ContainsKey(id)));
            if (found.IsEmpty) return filled;

            await repository.SaveKeywordsAsync(found);
            filled += found.Count;
        }
    }

    private async Task<List<string>?> FetchAsync(TitleAwaitingKeywords title, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            try
            {
                var keywords = await provider.FetchKeywordsAsync(title.TmdbId, title.IsShow, cancellationToken);
                if (keywords != null) return keywords;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fetching TMDB keywords for {TmdbId} from {Provider} failed.", title.TmdbId, provider.Id);
            }
        }
        return null;
    }
}
