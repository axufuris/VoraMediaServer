namespace Vora.Application.Media;

public static class EpisodeSequence
{
    public const int SpecialsSeason = 0;

    public static bool IsSpecial(int seasonNumber) => seasonNumber == SpecialsSeason;

    public static List<T> PreferRegular<T>(IEnumerable<T> episodes, Func<T, int> seasonOf)
    {
        var all = episodes.ToList();
        var regular = all.Where(e => !IsSpecial(seasonOf(e))).ToList();
        return regular.Count > 0 ? regular : all;
    }

    public static T? PickNextUp<T>(
        IEnumerable<T> unplayed,
        Func<T, int> seasonOf,
        Func<T, int> episodeOf,
        Func<T, bool> inProgress,
        bool showHasRegularEpisodes) where T : class
    {
        var candidates = unplayed.ToList();

        var specialInProgress = candidates
            .Where(e => IsSpecial(seasonOf(e)) && inProgress(e))
            .OrderBy(episodeOf)
            .FirstOrDefault();
        if (specialInProgress != null) return specialInProgress;

        var pool = showHasRegularEpisodes
            ? candidates.Where(e => !IsSpecial(seasonOf(e)))
            : candidates;

        return pool.OrderBy(seasonOf).ThenBy(episodeOf).FirstOrDefault();
    }
}
