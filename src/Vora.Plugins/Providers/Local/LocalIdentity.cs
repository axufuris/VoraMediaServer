using System.Globalization;
using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;

namespace Vora.Plugins.Providers.Local;

public static partial class LocalIdentity
{
    private const int YearTolerance = 1;

    public static MetadataResult? Trusted(MetadataResult? document, int? parsedYear, ExternalIdSet tagged)
    {
        if (document == null) return null;
        if (parsedYear is int parsed && document.ReleaseDate?.Year is int declared && Math.Abs(parsed - declared) > YearTolerance) return null;
        return Contradicts(Ids(document), tagged) ? null : document;
    }

    public static ExternalIdSet Ids(MetadataResult document) =>
        ExternalIdSet.From(Clean(document.TmdbId), Clean(document.ImdbId), Clean(document.TvdbId));

    public static bool Contradicts(ExternalIdSet left, ExternalIdSet right) =>
        Differs(left.TmdbId, right.TmdbId) || Differs(left.ImdbId, right.ImdbId) || Differs(left.TvdbId, right.TvdbId);

    public static bool SharesAnId(ExternalIdSet left, ExternalIdSet right) =>
        Same(left.TmdbId, right.TmdbId) || Same(left.ImdbId, right.ImdbId) || Same(left.TvdbId, right.TvdbId);

    public static ExternalIdSet Merge(ExternalIdSet preferred, ExternalIdSet fallback) =>
        ExternalIdSet.From(
            Clean(preferred.TmdbId) ?? Clean(fallback.TmdbId),
            Clean(preferred.ImdbId) ?? Clean(fallback.ImdbId),
            Clean(preferred.TvdbId) ?? Clean(fallback.TvdbId));

    public static ExternalIdSet TaggedIds(params string?[] names)
    {
        string? tmdbId = null, imdbId = null, tvdbId = null;
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (Match match in IdTag().Matches(name))
            {
                var id = Clean(match.Groups["Id"].Value);
                if (id == null) continue;
                switch (match.Groups["Provider"].Value.ToLowerInvariant())
                {
                    case "tmdb": tmdbId ??= id; break;
                    case "imdb": imdbId ??= id; break;
                    case "tvdb": tvdbId ??= id; break;
                }
            }
        }
        return ExternalIdSet.From(tmdbId, imdbId, tvdbId);
    }

    public static int? YearIn(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var match = BracketedYear().Match(name);
        return match.Success ? int.Parse(match.Groups["Year"].Value, CultureInfo.InvariantCulture) : null;
    }

    public static string TitleIn(string name)
    {
        var title = NamedTitle().Match(name).Groups["Title"].Value.Trim();
        return title.Length == 0 ? name : title;
    }

    private static bool Differs(string? left, string? right) =>
        Clean(left) is { } a && Clean(right) is { } b && !a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        Clean(left) is { } a && Clean(right) is { } b && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    [GeneratedRegex(@"[\{\[](?<Provider>imdb|tmdb|tvdb)(?:id)?[-=](?<Id>[^\}\]]+)[\}\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdTag();

    [GeneratedRegex(@"\((?<Year>(19|20)\d{2})\)", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedYear();

    [GeneratedRegex(@"^(?<Title>.*?)(?=\s*\((19|20)\d{2}\)|\s*\{|\s*\[|$)", RegexOptions.CultureInvariant)]
    private static partial Regex NamedTitle();
}
