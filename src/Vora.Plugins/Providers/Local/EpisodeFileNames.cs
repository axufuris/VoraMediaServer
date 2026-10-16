using System.Globalization;
using System.Text.RegularExpressions;

namespace Vora.Plugins.Providers.Local;

public sealed record EpisodeFileName(int? Season, int? Episode, int? EndEpisode, int? Absolute, DateOnly? AirDate, string? Title, bool NumbersGuessed);

public sealed record EpisodeNumbers(int Season, int Episode, int? EndEpisode);

public enum EpisodePlacementSource
{
    Name,
    Document,
    Guess
}

public sealed record EpisodePlacement(int Season, int Episode, int? EndEpisode, string? Title, DateOnly? AirDate, EpisodePlacementSource Source);

public sealed partial class EpisodeFileNames
{
    private static readonly Regex AnyNaming = new(LocalScannerPatterns.TvEpisodeAnyNaming, RegexOptions.IgnoreCase);
    private static readonly Regex NumberFirst = new(LocalScannerPatterns.TvEpisodeNumberFirst, RegexOptions.IgnoreCase);
    private static readonly string[] MonthDateFormats = ["MMM d yyyy", "MMMM d yyyy"];

    private readonly Regex _library;

    public EpisodeFileNames(string? libraryPattern)
    {
        PatternProblem = ProblemWith(libraryPattern);
        _library = new Regex(PatternProblem == null ? LocalScannerPatterns.EffectiveTvEpisode(libraryPattern) : LocalScannerPatterns.TvEpisode, RegexOptions.IgnoreCase);
    }

    public string? PatternProblem { get; }

    public static string? ProblemWith(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            var groups = new Regex(pattern, RegexOptions.IgnoreCase).GetGroupNames();
            return groups.Contains("Episode") || groups.Contains("AirDate")
                ? null
                : @"The pattern needs an Episode or AirDate group, like (?<Episode>\d+), so Vora knows which episode a file is.";
        }
        catch (ArgumentException ex)
        {
            return $"The pattern isn't a valid regular expression: {ex.Message}";
        }
    }

    public EpisodeFileName? Read(string fileName)
    {
        var library = _library.Match(fileName);
        var fromLibrary = library.Success ? FromLibraryPattern(library) : null;
        if (fromLibrary is { Episode: not null }) return fromLibrary;

        var any = AnyNaming.Match(fileName);
        var fromAnyNaming = any.Success ? FromAnyNaming(any) : null;
        if (fromAnyNaming is { NumbersGuessed: false }) return fromAnyNaming;

        if (fromLibrary is { AirDate: DateOnly date } && fromAnyNaming is { AirDate: DateOnly sameDate } && date == sameDate)
        {
            return fromAnyNaming with { Title = fromAnyNaming.Title ?? fromLibrary.Title };
        }

        return fromLibrary ?? fromAnyNaming;
    }

    public EpisodePlacement? Place(string fileName, string? folderName, Func<EpisodeNumbers?> fromDocument) =>
        PlaceByName(fileName, folderName, fromDocument) is { } placement
            ? placement with { Title = ReleaseNames.EpisodeTitle(fileName, placement.Title) }
            : null;

    private EpisodePlacement? PlaceByName(string fileName, string? folderName, Func<EpisodeNumbers?> fromDocument)
    {
        var name = Read(fileName);
        var folderSeason = SeasonOfFolder(folderName);

        if (name is { NumbersGuessed: false, Episode: int episode })
        {
            return new EpisodePlacement(name.Season ?? folderSeason ?? 1, episode, name.EndEpisode, name.Title, name.AirDate, EpisodePlacementSource.Name);
        }

        if (fromDocument() is { } document)
        {
            return new EpisodePlacement(document.Season, document.Episode, document.EndEpisode, name?.Title, name?.AirDate, EpisodePlacementSource.Document);
        }

        if (name?.Episode is int guessed)
        {
            return new EpisodePlacement(name.Season ?? folderSeason ?? 1, guessed, name.EndEpisode, name.Title, name.AirDate, EpisodePlacementSource.Guess);
        }

        if (name?.Absolute is int absolute)
        {
            return new EpisodePlacement(folderSeason ?? 1, absolute, name.EndEpisode, name.Title, name.AirDate, EpisodePlacementSource.Guess);
        }

        if (name?.AirDate is DateOnly date)
        {
            return new EpisodePlacement(folderSeason ?? date.Year, date.Year * 10000 + date.Month * 100 + date.Day, null, name.Title, date, EpisodePlacementSource.Guess);
        }

        if (folderSeason is int season && NumberFirst.Match(fileName) is { Success: true } numbered
            && int.TryParse(numbered.Groups["Episode"].Value, out var first) && first > 0)
        {
            return new EpisodePlacement(season, first, null, Title(numbered.Groups["EpisodeTitle"]), null, EpisodePlacementSource.Guess);
        }

        return null;
    }

    public static int? SeasonOfFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;
        if (folderName.StartsWith("Specials", StringComparison.OrdinalIgnoreCase)) return 0;
        var season = SeasonFolder().Match(folderName);
        return season.Success && int.TryParse(season.Groups["Season"].Value, out var number) ? number : null;
    }

    public static DateOnly? ParseAirDate(string text)
    {
        var numeric = NumericDate().Match(text.Trim());
        if (numeric.Success)
        {
            int Part(string group) => int.Parse(numeric.Groups[group].Value, CultureInfo.InvariantCulture);
            if (numeric.Groups["Year"].Success) return Date(Part("Year"), Part("Month"), Part("Day"));

            var (first, second, year) = (Part("First"), Part("Second"), Part("TrailingYear"));
            if (first > 12) return Date(year, second, first);
            if (second > 12) return Date(year, first, second);
            return null;
        }

        var spelled = string.Join(' ', text.Replace(".", " ").Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return DateOnly.TryParseExact(spelled, MonthDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var named) ? named : null;
    }

    private static EpisodeFileName FromLibraryPattern(Match match)
    {
        int? Number(string group) => match.Groups[group].Success && int.TryParse(match.Groups[group].Value, out var value) ? value : null;
        var episode = Number("Episode");
        var end = Number("EndEpisode");
        DateOnly? airDate = !match.Groups["AirDate"].Success ? null
            : ParseAirDate(match.Groups["AirDate"].Value)
                ?? (DateOnly.TryParse(match.Groups["AirDate"].Value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null);

        return new EpisodeFileName(Number("Season"), episode, end > (episode ?? 1) ? end : null, Number("Absolute"), airDate, Title(match.Groups["EpisodeTitle"]), false);
    }

    private static EpisodeFileName FromAnyNaming(Match match)
    {
        int? First(string group) => match.Groups[group].Captures.Count > 0 && int.TryParse(match.Groups[group].Captures[0].Value, out var value) ? value : null;
        int? Last(string group) => match.Groups[group].Captures.Count > 0 && int.TryParse(match.Groups[group].Captures[^1].Value, out var value) ? value : null;

        var title = Title(match.Groups["EpisodeTitle"]);
        var airDate = match.Groups["AirDate"].Captures.Count > 0 ? ParseAirDate(match.Groups["AirDate"].Captures[0].Value) : null;
        var absolute = First("Absolute");

        if (First("Episode") is int episode)
        {
            return new EpisodeFileName(First("Season"), episode, Wider(Last("EndEpisode"), episode), absolute, airDate, title, false);
        }

        if (First("GuessedEpisode") is int guessed)
        {
            return new EpisodeFileName(First("GuessedSeason"), guessed, Wider(Last("EndEpisode"), guessed), absolute, airDate, title, true);
        }

        return new EpisodeFileName(null, null, absolute is int start ? Wider(Last("EndEpisode"), start) : null, absolute, airDate, title, true);
    }

    private static int? Wider(int? end, int start) => end > start ? end : null;

    private static string? Title(Group group) =>
        group.Success && !string.IsNullOrWhiteSpace(group.Value) ? group.Value.Trim(' ', '.', '-') : null;

    private static DateOnly? Date(int year, int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month) && year is >= 1 and <= 9999
            ? new DateOnly(year, month, day)
            : null;

    [GeneratedRegex(@"^(?:season|series)[ ._-]*(?<Season>\d{1,4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"^(?:(?<Year>\d{4})[-._](?<Month>\d{2})[-._](?<Day>\d{2})|(?<First>\d{2})[-._](?<Second>\d{2})[-._](?<TrailingYear>\d{4}))$")]
    private static partial Regex NumericDate();
}
