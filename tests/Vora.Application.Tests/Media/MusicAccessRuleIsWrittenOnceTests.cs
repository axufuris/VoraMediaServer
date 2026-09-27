using System.Text.RegularExpressions;

namespace Vora.Application.Tests.Media;

// The music rating rule was written out in each repository, and the copies
// drifted into three different rules — one of which let Explicit tracks through
// to profiles restricted to Clean. Parental controls are only as strong as their
// weakest copy, so this fails the build if a second copy appears.
//
// It looks for the two expressions a rating filter cannot be written without:
// comparing ContentRating to null, and testing it against the allowlist.
public class MusicAccessRuleIsWrittenOnceTests
{
    private const string TheOnePlace = "MusicAccessQuery.cs";

    private static readonly Regex RatingRule = new(
        @"ContentRating\s*[!=]=\s*null|AllowedRatings\s*\.\s*Contains|allowed\w*\s*\.\s*Contains\(\s*\w+\.ContentRating",
        RegexOptions.Compiled);

    private static IEnumerable<FileInfo> MusicPersistenceFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        dir.Should().NotBeNull();

        var repositories = new DirectoryInfo(Path.Combine(dir!.FullName, "src", "Vora.Infrastructure", "Persistence", "Repositories"));

        // The smart playlist evaluator is where the fourth copy was — it is not a
        // music repository by name, but it filters tracks, so it is in scope.
        return repositories.GetFiles("Music*.cs")
            .Concat(repositories.GetFiles("SmartPlaylistEvaluator.cs"));
    }

    [Fact]
    public void The_music_rating_rule_exists_in_exactly_one_place()
    {
        var copies = MusicPersistenceFiles()
            .Where(f => f.Name != TheOnePlace)
            .SelectMany(f => File.ReadAllLines(f.FullName)
                .Select((line, i) => (line, i))
                .Where(x => RatingRule.IsMatch(x.line))
                .Select(x => $"{f.Name}:{x.i + 1}  {x.line.Trim()}"))
            .ToList();

        copies.Should().BeEmpty(
            "music access is decided in MusicAccessQuery — use ApplyMusicAccess or ApplyMusicRatings rather than " +
            "re-deriving it, or the copies drift and a restricted profile gets through one of them");
    }

    [Fact]
    public void The_one_place_still_holds_the_rule()
    {
        var home = MusicPersistenceFiles().SingleOrDefault(f => f.Name == TheOnePlace);
        home.Should().NotBeNull();
        RatingRule.IsMatch(File.ReadAllText(home!.FullName)).Should().BeTrue(
            "otherwise the scan above passes by finding nothing anywhere");
    }
}
