using System.Text.RegularExpressions;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class EpisodeFileNameTests
{
    private static readonly EpisodeFileNames Automatic = new(null);

    private static readonly Dictionary<string, string> ReadFirstByTheDefault = new()
    {
        ["Example Show - 001 - S01E01 - The Beginning.mkv"] = "S=01 E=01 T=The Beginning",
        ["Example Show - 001 - S01E01 - 2024.01.15 - The Beginning.mkv"] = "S=01 E=01 D=2024.01.15 T=The Beginning",
    };

    public static TheoryData<string, string> Jc21Cases()
    {
        var cases = new TheoryData<string, string>();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Scanner", "tvshowregex-cases.txt")).Where(l => l.Contains(" | ")))
        {
            var parts = line.Split(" | ", 2);
            cases.Add(parts[0], ReadFirstByTheDefault.GetValueOrDefault(parts[0], parts[1]));
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Jc21Cases))]
    public void Every_naming_in_jc21s_tvshowregex_cases_is_read(string fileName, string expected)
    {
        Describe(Automatic.Read(Path.GetFileNameWithoutExtension(fileName))).Should().Be(Expected(expected));
    }

    [Theory]
    [InlineData("1883 (2021) - S01E01 - 1883 [Bluray-1080p Remux][TrueHD 5.1][AVC]-MJOLNiR", "S=1 E=1 T=1883")]
    [InlineData("Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol [HDTV-720p][AAC 2.0][x264]-NGP", "S=5 E=9 EE=10 T=Its a Wonderful Christmas Carol")]
    [InlineData("Red vs. Blue (2003) - S02E01-E02-E03 - Everything Old Is New Again", "S=2 E=1 EE=3 T=Everything Old Is New Again")]
    [InlineData("Lost (2004) - S06E17 - E18 - The End", "S=6 E=17 EE=18 T=The End")]
    [InlineData("One Piece (1999) - S01E01 - 001 - I'm Luffy [Bluray-1080p][FLAC 2.0][x265]-group", "S=1 E=1 A=1 T=I'm Luffy")]
    [InlineData("Doctor Who (2005) - S00E01 - The Christmas Invasion [WEBDL-1080p]", "S=0 E=1 T=The Christmas Invasion")]
    [InlineData("S01E03 - And the Bag's in the River", "S=1 E=3 T=And the Bag's in the River")]
    [InlineData("2024-01-15 - Some Episode", "D=2024-01-15 T=Some Episode")]
    public void Sonarr_names_with_season_and_episode_markers_read_the_way_they_always_have(string fileName, string expected)
    {
        Describe(Automatic.Read(fileName)).Should().Be(expected);
    }

    [Theory]
    [InlineData("Grey's Anatomy (2005) - S14E09 - 1-800-799-7233 [AMZN WEBDL-1080p][EAC3 5.1][h264]-NTb", "1-800-799-7233")]
    [InlineData("Marvel's Agents of S.H.I.E.L.D. (2013) - S01E02 - 0-8-4 [Bluray-1080p Remux][DTS-HD MA 5.1][AVC]-FraMeSToR", "0-8-4")]
    [InlineData("Chernobyl (2019) - S01E01 - 1-23-45", "1-23-45")]
    [InlineData("Franklin & Bash (2011) - S02E10 - 6-50 to SLC", "6-50 to SLC")]
    public void A_title_that_starts_with_numbers_keeps_them(string fileName, string title)
    {
        Automatic.Read(fileName)?.Title.Should().Be(title);
    }

    [Theory]
    [InlineData("Example Show - S01E01-S01E02 - Double Episode", 1, 2)]
    [InlineData("Example Show - S01E01-02 - Double Episode", 1, 2)]
    [InlineData("Example Show - S01E01 & S01E02 - Double Episode", 1, 2)]
    [InlineData("Example Show - S01E01+E02 - Double Episode", 1, 2)]
    [InlineData("Example Show - S01E01_E02 - Double Episode", 1, 2)]
    public void Every_way_Sonarr_writes_a_multi_episode_file_is_one_range(string fileName, int first, int last)
    {
        var read = Automatic.Read(fileName);

        read?.Episode.Should().Be(first);
        read?.EndEpisode.Should().Be(last);
    }

    [Fact]
    public void A_custom_pattern_still_decides_first()
    {
        var custom = new EpisodeFileNames(@"^(?<EpisodeTitle>.+?) \[(?<Season>\d+)\.(?<Episode>\d+)\]$");

        Describe(custom.Read("Pilot [2.07]")).Should().Be("S=2 E=7 T=Pilot");
        Describe(custom.Read("Ted Lasso.1x2 - Biscuits")).Should().Be("S=1 E=2 T=Biscuits");
    }

    [Theory]
    [InlineData("Ted Lasso.1x1 - Pilot", "Season 1", 1, 1)]
    [InlineData("Ted Lasso.2x9 - Beard After Hours", "Season 2", 2, 9)]
    [InlineData("S02E09", "Ted Lasso", 2, 9)]
    [InlineData("1x01 - Pilot", "Season 1", 1, 1)]
    public void An_episode_marker_places_the_file(string fileName, string folder, int season, int episode)
    {
        var placement = Automatic.Place(fileName, folder, () => null);

        placement.Should().NotBeNull();
        (placement?.Season, placement?.Episode).Should().Be((season, episode));
    }

    [Fact]
    public void A_name_with_no_episode_marker_uses_the_numbers_in_the_nfo()
    {
        var placement = Automatic.Place("Pilot", "Season 2", () => new EpisodeNumbers(2, 5, 6));

        (placement?.Season, placement?.Episode, placement?.EndEpisode).Should().Be((2, 5, 6));
    }

    [Fact]
    public void A_marker_in_the_name_wins_over_the_nfo()
    {
        var asked = false;
        var placement = Automatic.Place("Show - S03E04 - Title", "Season 3", () => { asked = true; return new EpisodeNumbers(9, 9, null); });

        (placement?.Season, placement?.Episode).Should().Be((3, 4));
        asked.Should().BeFalse();
    }

    [Fact]
    public void The_nfo_wins_over_a_guess_from_a_bare_number()
    {
        var placement = Automatic.Place("Example Show 101 Pilot", "Season 1", () => new EpisodeNumbers(4, 12, null));

        (placement?.Season, placement?.Episode).Should().Be((4, 12));
    }

    [Theory]
    [InlineData("Example Show 101 Pilot", "Season 1", 1, 1)]
    [InlineData("Example Show - 012 - The Beginning", "Season 3", 3, 12)]
    [InlineData("Example Show - Ep07 - Seven", null, 1, 7)]
    [InlineData("05 - Pilot", "Season 2", 2, 5)]
    public void Without_an_nfo_a_guessed_number_places_the_file_in_its_folders_season(string fileName, string? folder, int season, int episode)
    {
        var placement = Automatic.Place(fileName, folder, () => null);

        (placement?.Season, placement?.Episode).Should().Be((season, episode));
    }

    [Fact]
    public void A_number_first_name_outside_a_season_folder_is_not_guessed()
    {
        Automatic.Place("24 - Pilot", "24", () => null).Should().BeNull();
    }

    [Fact]
    public void A_daily_show_gets_an_episode_per_air_date_in_the_year_or_its_folders_season()
    {
        var bare = Automatic.Place("Daily Show - 2024-01-15 - Guest Name", "Daily Show", () => null);
        var inFolder = Automatic.Place("Daily Show - 2024-01-16 - Another Guest", "Season 29", () => null);

        (bare?.Season, bare?.Episode, bare?.AirDate).Should().Be((2024, 20240115, new DateOnly(2024, 1, 15)));
        (inFolder?.Season, inFolder?.Episode).Should().Be((29, 20240116));
    }

    [Fact]
    public void A_name_with_nothing_to_go_on_is_not_placed()
    {
        Automatic.Place("Bonus Material Final", "Season 1", () => null).Should().BeNull();
    }

    [Theory]
    [InlineData("Season 1", 1)]
    [InlineData("Season 02", 2)]
    [InlineData("season.3", 3)]
    [InlineData("Series 4", 4)]
    [InlineData("Specials", 0)]
    [InlineData("Ted Lasso", null)]
    [InlineData("Seasonal Specials", null)]
    public void A_season_folder_gives_its_number(string folder, int? season)
    {
        EpisodeFileNames.SeasonOfFolder(folder).Should().Be(season);
    }

    [Theory]
    [InlineData("2024-01-15", 2024, 1, 15)]
    [InlineData("2024.01.15", 2024, 1, 15)]
    [InlineData("2024_01_15", 2024, 1, 15)]
    [InlineData("15-01-2024", 2024, 1, 15)]
    [InlineData("01-15-2024", 2024, 1, 15)]
    [InlineData("Jan 15 2024", 2024, 1, 15)]
    [InlineData("January 15, 2024", 2024, 1, 15)]
    public void An_air_date_reads_in_the_forms_people_write_it(string text, int year, int month, int day)
    {
        EpisodeFileNames.ParseAirDate(text).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("03-04-2024")]
    [InlineData("2024-02-30")]
    public void An_air_date_that_cannot_be_told_apart_or_does_not_exist_is_not_guessed(string text)
    {
        EpisodeFileNames.ParseAirDate(text).Should().BeNull();
    }

    private static string Describe(EpisodeFileName? read)
    {
        if (read == null) return "none";
        var parts = new List<string>();
        if (read.Season is int season) parts.Add($"S={season}");
        if (read.Episode is int episode) parts.Add($"E={episode}");
        if (read.EndEpisode is int end) parts.Add($"EE={end}");
        if (read.Absolute is int absolute) parts.Add($"A={absolute}");
        if (read.AirDate is DateOnly date) parts.Add($"D={date:yyyy-MM-dd}");
        if (read.Title != null) parts.Add($"T={read.Title}");
        return string.Join(" ", parts);
    }

    private static string Expected(string jc21)
    {
        var parts = new List<string>();
        int? Number(string key) => Regex.Match(jc21, $@"\b{key}=(\d+)\b") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
        if (Number("S") is int season) parts.Add($"S={season}");
        if (Number("E") is int episode) parts.Add($"E={episode}");
        if (Number("EE") is int end) parts.Add($"EE={end}");
        if (Number("A") is int absolute) parts.Add($"A={absolute}");
        if (Regex.Match(jc21, @"\bD=(.+?)(?= T=|$)") is { Success: true } date)
        {
            parts.Add($"D={EpisodeFileNames.ParseAirDate(date.Groups[1].Value):yyyy-MM-dd}");
        }
        if (Regex.Match(jc21, @"\bT=(.+)$") is { Success: true } title) parts.Add($"T={title.Groups[1].Value}");
        return string.Join(" ", parts);
    }
}
