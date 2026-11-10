namespace Vora.Plugins.Providers.Tmdb;

// The Genres table is keyed by TMDB's genre ids. A provider with ids of its own
// has to be read by name: TVDB's 12 is Drama and TMDB's is Adventure, so taking
// TVDB's ids as they were gave Breaking Bad "Adventure, Fantasy" and dropped
// Comedy, which has no TMDB twin at 15, from every show.
public static class TmdbGenres
{
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = 28, ["Adventure"] = 12, ["Animation"] = 16, ["Comedy"] = 35,
        ["Crime"] = 80, ["Documentary"] = 99, ["Drama"] = 18, ["Family"] = 10751,
        ["Fantasy"] = 14, ["History"] = 36, ["Horror"] = 27, ["Music"] = 10402,
        ["Mystery"] = 9648, ["Romance"] = 10749, ["Science Fiction"] = 878,
        ["TV Movie"] = 10770, ["Thriller"] = 53, ["War"] = 10752, ["Western"] = 37,
        ["Action & Adventure"] = 10759, ["Kids"] = 10762, ["News"] = 10763,
        ["Reality"] = 10764, ["Sci-Fi & Fantasy"] = 10765, ["Soap"] = 10766,
        ["Talk"] = 10767, ["War & Politics"] = 10768
    };

    // TVDB's names for what TMDB calls something else. Its formats with no TMDB
    // genre (Mini-Series, Sport, Indie, Podcast, Awards Show) are left out.
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Science-Fiction"] = 878, ["Sci-Fi"] = 878,
        ["Children"] = 10762, ["Talk Show"] = 10767, ["Musical"] = 10402,
        ["Suspense"] = 53, ["Anime"] = 16, ["Martial Arts"] = 28,
        ["Game Show"] = 10764, ["Home and Garden"] = 10764, ["Food"] = 10764,
        ["Travel"] = 99
    };

    public static int? IdFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        return ByName.TryGetValue(trimmed, out var id) || Aliases.TryGetValue(trimmed, out id) ? id : null;
    }

    // Distinct ids, in the order the names came, unknown names dropped.
    public static List<int> IdsFor(IEnumerable<string?> names) =>
        names.Select(IdFor).OfType<int>().Distinct().ToList();
}
