using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Recommendations.Providers;

// The For You rows built from the library alone: titles like the ones the
// profile watched lately or rated highly (through the embeddings the nightly AI
// task stores, so no AI call is made here), their favourite genres weighed by
// their ratings, and the best-rated titles they haven't seen. No title is in two
// rows, and nothing they have started or rated is suggested.
public class LocalRecommendationProvider(IRecommendationRepository repository) : IRecommendationProvider
{
    internal const int ItemsPerRow = 15;
    internal const int MinItemsPerRow = 4;
    internal const int WatchedRows = 3;
    internal const int LovedRows = 2;
    internal const int GenreRows = 4;

    // Fetched per row, more than it shows, so a row whose best titles went to an
    // earlier row can still fill.
    private const int Candidates = 40;

    // Titles without an embedding have nothing to be compared with. A few misses
    // in a row means the embeddings are not there, so stop asking.
    private const int SeedMisses = 3;

    public string Id => "local_recommendations";
    public string Name => "Vora Local Recommendations";
    public string Description => "Builds rows from your watch history and ratings: titles like ones you watched or rated highly, your favourite genres, and the best-rated titles you haven't seen.";
    public string Version => "2.0.0";
    public string Type => "Recommendation";
    public string DeveloperName => "System";
    public bool IsSystemPlugin => true;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() =>
        new List<PluginSettingDefinitionDto>();

    public async Task<IEnumerable<RecommendationListDto>> GetRecommendationsAsync(Guid profileId, Guid? libraryId, CancellationToken cancellationToken = default)
    {
        var watched = await repository.GetWatchedTitlesAsync(profileId, libraryId);
        if (watched.Count == 0) return new List<RecommendationListDto>();

        var seeds = new HashSet<Guid>();
        var recent = watched
            .Where(t => t.LastWatchedAt.HasValue && !(t.Rating <= TasteRatings.Disliked))
            .OrderByDescending(t => t.LastWatchedAt);
        var becauseWatched = await SimilarRowsAsync(recent, WatchedRows, seeds, profileId, libraryId,
            t => $"Because you watched {t.Title}", t => $"Like {t.Title}, and not seen yet.", cancellationToken);

        var loved = watched
            .Where(t => t.Rating >= TasteRatings.Loved)
            .OrderByDescending(t => t.RatedAt ?? t.LastWatchedAt);
        var becauseLoved = await SimilarRowsAsync(loved, LovedRows, seeds, profileId, libraryId,
            t => $"Because you rated {t.Title} highly", t => $"Titles like {t.Title}, which you rated {t.Rating:0.#}/10.", cancellationToken);

        var genreRows = new List<Row>();
        foreach (var genre in TopGenres(watched, await repository.GetGenresAsync(watched.Select(t => t.Id).ToList()), GenreRows))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = await repository.GetTopUnwatchedByGenreAsync(genre.Id, profileId, libraryId, Candidates);
            genreRows.Add(new Row($"Because you like {genre.Name}", $"The best-rated {genre.Name} you haven't seen.", ids));
        }

        var topRated = new Row("Top rated you haven't seen", "The best-rated titles here you haven't started.",
            await repository.GetTopUnwatchedAsync(profileId, libraryId, Candidates));

        return Fill(Interleave(becauseWatched, genreRows, becauseLoved).Append(topRated));
    }

    // Rows of titles near each seed, most recent seed first, skipping seeds that
    // have nothing near them (no embedding yet, or every neighbour seen).
    private async Task<List<Row>> SimilarRowsAsync(
        IEnumerable<WatchedTitle> candidates, int wanted, HashSet<Guid> seeds, Guid profileId, Guid? libraryId,
        Func<WatchedTitle, string> title, Func<WatchedTitle, string> description, CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        var misses = 0;
        foreach (var seed in candidates)
        {
            if (rows.Count == wanted || misses == SeedMisses) break;
            if (seeds.Contains(seed.Id)) continue;
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await repository.GetSimilarUnwatchedAsync(seed.Id, profileId, libraryId, Candidates);
            if (ids.Count < MinItemsPerRow)
            {
                misses++;
                continue;
            }

            seeds.Add(seed.Id);
            rows.Add(new Row(title(seed), description(seed), ids));
        }
        return rows;
    }

    // Each watched title counts once for each of its genres, twice when it was
    // rated highly and not at all when it was rated low.
    internal static List<(int Id, string Name)> TopGenres(IReadOnlyList<WatchedTitle> watched, IReadOnlyList<TitleGenre> genres, int count)
    {
        var score = watched.ToDictionary(t => t.Id, t => t.Rating >= TasteRatings.Loved ? 2 : t.Rating <= TasteRatings.Disliked ? 0 : 1);
        return genres
            .Where(g => score.ContainsKey(g.TitleId))
            .GroupBy(g => (g.GenreId, g.GenreName))
            .Select(g => (g.Key.GenreId, g.Key.GenreName, Score: g.Sum(x => score[x.TitleId])))
            .Where(g => g.Score > 0)
            .OrderByDescending(g => g.Score)
            .ThenBy(g => g.GenreName, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(g => (g.GenreId, g.GenreName))
            .ToList();
    }

    // Watched, genre, loved, watched, genre, loved... so the page doesn't open
    // with three rows of the same kind.
    internal static IEnumerable<Row> Interleave(params IReadOnlyList<Row>[] kinds)
    {
        for (var i = 0; i < kinds.Max(k => k.Count); i++)
        {
            foreach (var kind in kinds)
            {
                if (i < kind.Count) yield return kind[i];
            }
        }
    }

    // In page order, each row takes the titles no earlier row took. A row left
    // with too few is dropped. The weight is the row's place: the clients sort
    // ascending.
    internal static List<RecommendationListDto> Fill(IEnumerable<Row> rows)
    {
        var used = new HashSet<Guid>();
        var lists = new List<RecommendationListDto>();
        foreach (var row in rows)
        {
            var items = row.Candidates.Where(id => !used.Contains(id)).Distinct().Take(ItemsPerRow).ToList();
            if (items.Count < MinItemsPerRow) continue;

            used.UnionWith(items);
            lists.Add(new RecommendationListDto
            {
                Title = row.Title,
                Description = row.Description,
                Weight = lists.Count + 1,
                LocalItemIds = items
            });
        }
        return lists;
    }

    internal sealed record Row(string Title, string Description, List<Guid> Candidates);
}
