using Vora.Application.Media;

namespace Vora.Application.SmartLists;

public enum TvLevel
{
    Show,
    Season,
    Episode
}

public sealed record TvCandidate(
    Guid Id,
    TvLevel Level,
    Guid ShowId,
    Guid? SeasonId,
    int SeasonNumber,
    int EpisodeNumber,
    DateTime AddedAt,
    DateTime? LastContentAddedAt,
    bool Played);

public static class RecentlyAddedTv
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(2);

    public static HashSet<Guid> OnePerShow(IEnumerable<TvCandidate> mostRecentFirst) =>
        mostRecentFirst
            .GroupBy(c => c.ShowId)
            .Select(show => Choose(show.ToList()).Id)
            .ToHashSet();

    public static TvCandidate PickEpisode(IReadOnlyCollection<TvCandidate> episodes)
    {
        var pool = EpisodeSequence.PreferRegular(episodes, e => e.SeasonNumber);
        return pool.Where(e => !e.Played).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).FirstOrDefault()
            ?? pool.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).First();
    }

    private static TvCandidate Choose(List<TvCandidate> items)
    {
        var series = items.FirstOrDefault(c => c.Level == TvLevel.Show);
        if (series != null && IsNewShow(series)) return series;

        var latest = items.Max(c => c.LastContentAddedAt ?? c.AddedAt);
        var seasons = items.Where(c => c.Level == TvLevel.Season).ToList();
        var episodes = items.Where(c => c.Level == TvLevel.Episode && latest - c.AddedAt <= RecentWindow).ToList();
        if (episodes.Count == 0) return seasons.FirstOrDefault() ?? series ?? items[0];

        var newSeasons = episodes.Select(e => e.SeasonId).Distinct().ToList();
        if (newSeasons.Count > 1) return series ?? seasons.FirstOrDefault() ?? PickEpisode(episodes);
        if (episodes.Count == 1) return episodes[0];
        return seasons.FirstOrDefault(s => s.Id == newSeasons[0]) ?? PickEpisode(episodes);
    }

    private static bool IsNewShow(TvCandidate series) =>
        series.LastContentAddedAt is not { } latest || latest - series.AddedAt <= RecentWindow;
}
