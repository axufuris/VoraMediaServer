namespace Vora.Plugins.Providers.Local;

public static class LocalScannerPatterns
{
    public const string TvEpisode =
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:(?:\s*-\s*|-?|\+|_|\s*&\s*)(?:[sS]\d{1,4})?[eE](?<EndEpisode>\d{1,4})|-(?<EndEpisode>\d{1,4})(?=\s*-\s))*(?:\s*-\s*(?<Absolute>\d{1,4})(?![-._]?\d))?(?:\s*-\s*(?<AirDate>\d{4}[-._]\d{2}[-._]\d{2}))?|(?<AirDate>\d{4}-\d{2}-\d{2})(?:\s*-\s*(?<Absolute>\d{1,4})(?![-._]?\d))?)\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";

    public const string TvEpisodeAnyNaming =
        @"(?:(?:^(?=(?:s(?:eason|eries)?[ ._]?)?\d{1,4}[ ._]?(?:x|e(?:p(?:isode)?)?)[ ._]?\d)|[ ._]*-[ ._]*|[ ._]+)(?:(?<AirDate>\d{4}[-._]\d{2}[-._]\d{2}|\d{2}[-._]\d{2}[-._]\d{4}|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?[ ._]\d{1,2},?[ ._]\d{4})|(?:(?:s(?:eason|eries)?[ ._]?)?(?<Season>\d{1,4})[ ._]?(?:x|e(?:p(?:isode)?)?)[ ._]?(?<Episode>\d{1,4})|(?<GuessedSeason>[1-9])\.?(?<GuessedEpisode>\d{2})|(?:absolute[ ._]?|ep?)?(?<Absolute>\d{1,4}))(?:(?:[-+_]?(?:s?\d{1,4}[ex]|ep?)|[-+]|[ ._]*&[ ._]*(?:s?\d{1,4}[ex]|ep?)?)(?<EndEpisode>\d{1,4}))*))+(?:(?:[ ._]*-[ ._]*|[ ._]+)(?<EpisodeTitle>.*?))??(?:(?:[ ._]*\[|[ ._](?:\d{3,4}p|web-?dl|webrip|blu-?ray|hdtv)\b).*)?(?:\.(?:mkv|mp4|m4v|avi|mov|wmv|mpe?g|ts|m2ts|mts|webm|3gp|flv|divx|ogv))?$";

    public const string TvEpisodeNumberFirst =
        @"^(?<Episode>\d{1,3})[ ._]*-[ ._]*(?<EpisodeTitle>.+?)(?:(?:[ ._]*\[|[ ._](?:\d{3,4}p|web-?dl|webrip|blu-?ray|hdtv)\b).*)?$";

    private static readonly string[] RetiredTvEpisodePatterns =
    [
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*[eE](?<EndEpisode>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:(?:\s*-\s*|-?)[eE](?<EndEpisode>\d{1,4}))*(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
    ];

    public static bool IsBuiltInTvEpisode(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
        || configured.Trim() == TvEpisode
        || RetiredTvEpisodePatterns.Contains(configured.Trim(), StringComparer.Ordinal);

    public static string EffectiveTvEpisode(string? configured) =>
        configured == null || IsBuiltInTvEpisode(configured) ? TvEpisode : configured;
}
