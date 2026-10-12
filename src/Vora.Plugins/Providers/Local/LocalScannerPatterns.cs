namespace Vora.Plugins.Providers.Local;

public static class LocalScannerPatterns
{
    public const string TvEpisode =
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:(?:\s*-\s*|-?)[eE](?<EndEpisode>\d{1,4}))*(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";

    private static readonly string[] RetiredTvEpisodePatterns =
    [
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
        @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*[eE](?<EndEpisode>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$",
    ];

    public static string EffectiveTvEpisode(string? configured) =>
        string.IsNullOrWhiteSpace(configured) || RetiredTvEpisodePatterns.Contains(configured.Trim(), StringComparer.Ordinal)
            ? TvEpisode
            : configured;
}
