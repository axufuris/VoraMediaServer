using System.Text.RegularExpressions;

namespace Vora.Application.Tests.Media;

// A vocabulary nobody is obliged to use decays. SupportedLibraryKinds was
// declared on every plugin and read by nothing for as long as it existed, and
// the provider filter rule ended up written three times in three ways. This scans
// the source so a new `m is Movie || m is TvShow` has to be either a capability
// or a deliberate exception, rather than quietly routing around the vocabulary.
//
// Exemptions are keyed on the line itself, not on the file. A file-level
// exemption for MediaRepository would silence fifteen hundred lines to excuse
// one of them, and would keep passing after the line it was written for had gone
// — which is how the first draft of this test ended up with an entry pointing at
// code that no longer existed.
//
// Deliberately narrow. It matches only the set-membership shape that stands in
// for a capability, not the polymorphic projection in view models
// (`item is Episode ? ((Episode)item).Season.TvShow.LogoUrl : …`), which is a
// legitimate read of a subclass field, nor the per-type mappings such as the
// overlay sweep pairing each type with its own template.
public class NoBehaviouralTypeGuardsTests
{
    // `x is Movie || x is TvShow` — the shape that names a set instead of asking
    // a question. Whitespace-tolerant, identifier-agnostic.
    private static readonly Regex MembershipGuard = new(
        @"is\s+(Movie|TvShow|Season|Episode|Track)\s*\|\|\s*\w+(\.\w+)*\s+is\s+(Movie|TvShow|Season|Episode|Track)",
        RegexOptions.Compiled);

    // Exact source lines where this shape is the right answer, each with the
    // reason it is. Matched on the trimmed line, so editing the line retires the
    // exemption with it.
    private static readonly IReadOnlyDictionary<string, string> Allowed = new Dictionary<string, string>
    {
        // GetMediaIdsMissingMetadataAsync asks which FIELDS make each type
        // incomplete, and the answer differs per type: a film wants a release
        // date and a cast, an episode wants neither. That is a per-type rule like
        // the overlay sweep's template lookup, not one capability wearing a set
        // of class names — and it sits inside an OR branch, so it could not be
        // hoisted into its own Where even if it were.
        ["((m is Movie || m is TvShow) &&"] = "per-type field-completeness rules inside an OR branch",
    };

    private static DirectoryInfo SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        dir.Should().NotBeNull("the test has to be able to find the source tree");
        return new DirectoryInfo(Path.Combine(dir!.FullName, "src"));
    }

    private static IEnumerable<FileInfo> ScannedFiles() =>
        SourceRoot().GetFiles("*.cs", SearchOption.AllDirectories).Where(file =>
        {
            var path = file.FullName.Replace('\\', '/');
            if (path.Contains("/bin/") || path.Contains("/obj/")) return false;
            if (path.Contains("/Migrations/")) return false;
            // Projection lives in view models and is explicitly out of scope.
            if (path.Contains("/ViewModels/")) return false;
            // The capabilities themselves are where the shape is supposed to live.
            return file.Name != "MediaCapabilities.cs";
        });

    private static List<string> FindGuards(bool includeAllowed) =>
        ScannedFiles()
            .SelectMany(file => File.ReadAllLines(file.FullName)
                .Select((line, i) => (Line: line.Trim(), Number: i + 1))
                .Where(x => MembershipGuard.IsMatch(x.Line))
                .Where(x => includeAllowed || !Allowed.ContainsKey(x.Line))
                .Select(x => $"{file.Name}:{x.Number}  {x.Line}"))
            .ToList();

    [Fact]
    public void No_unexempted_set_membership_guard_stands_in_for_a_capability()
    {
        FindGuards(includeAllowed: false).Should().BeEmpty(
            "a set of media types named inline says which classes can do something and never which thing they can do. "
            + "Use a MediaCapabilities predicate — MediaCapabilities.X.On(e => e.MediaItem) reaches one through a "
            + "navigation property — or add the exact line to Allowed with the reason the shape is right there.");
    }

    // An allow-list that outlives its entries stops meaning anything, so every
    // exemption has to still be earning it.
    [Fact]
    public void Every_exemption_still_matches_a_line_in_the_source()
    {
        var found = FindGuards(includeAllowed: true);

        foreach (var (line, reason) in Allowed)
        {
            found.Should().Contain(
                f => f.EndsWith(line, StringComparison.Ordinal),
                $"'{line}' is exempted for '{reason}' but no longer appears in the source — drop the exemption");
        }
    }

    // The scanner has to be able to see a guard at all, or it passes by finding
    // nothing and proves only that it ran.
    [Fact]
    public void The_scanner_recognises_the_shape_it_is_looking_for()
    {
        MembershipGuard.IsMatch(".Where(m => m is Movie || m is TvShow);").Should().BeTrue();
        MembershipGuard.IsMatch(".Where(e => e.MediaItem is Movie || e.MediaItem is TvShow);").Should().BeTrue();
        MembershipGuard.IsMatch("x => x.Media is TvShow || x.Media is Season || x.Media is Episode").Should().BeTrue();
        MembershipGuard.IsMatch("((m is Movie || m is TvShow) &&").Should().BeTrue();

        MembershipGuard.IsMatch("item is Episode ? \"Episode\" : \"Unknown\",").Should().BeFalse();
        MembershipGuard.IsMatch(".Where(MediaCapabilities.IsBrowsableTitle);").Should().BeFalse();
        MembershipGuard.IsMatch(".Where(MediaCapabilities.IsPartOfATvShow.On((SessionMediaPair x) => x.Media));")
            .Should().BeFalse();
    }
}
