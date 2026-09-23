using System.Text.RegularExpressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

// The player carries its own copy of the threshold so it does not post a listen
// the server will discard. The server is authoritative, so drift is not
// symmetrical but it is bad in both directions: a stricter client silently loses
// plays that would have counted, and a looser one generates requests that are
// rejected and history that never appears.
//
// This reads the real .ts rather than restating its values, so editing one side
// alone fails here instead of at run time.
public class PlayQualificationParityTests
{
    private static string ClientSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        dir.Should().NotBeNull("the test has to be able to find the source tree");

        var path = Path.Combine(dir!.FullName, "src", "Vora.Web", "src", "utils", "playQualification.ts");
        File.Exists(path).Should().BeTrue($"the client copy of the rule should be at {path}");
        return File.ReadAllText(path);
    }

    private static double ConstantNamed(string name)
    {
        var match = Regex.Match(ClientSource(), $@"export const {name}\s*=\s*([0-9.]+)\s*;");
        match.Success.Should().BeTrue($"{name} should be exported from playQualification.ts");
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void The_long_listen_threshold_matches() =>
        ConstantNamed("LONG_LISTEN_SECONDS").Should().Be(PlayQualification.LongListenSeconds);

    [Fact]
    public void The_minimum_fraction_matches() =>
        ConstantNamed("MINIMUM_FRACTION").Should().Be(PlayQualification.MinimumFraction);

    [Fact]
    public void The_unknown_duration_fallback_matches() =>
        ConstantNamed("UNKNOWN_DURATION_SECONDS").Should().Be(PlayQualification.UnknownDurationSeconds);

    // The shape of the test matters as much as the numbers: a client that used
    // the constants in a different comparison would pass the three above and
    // still disagree with the server.
    [Fact]
    public void The_client_applies_them_the_same_way()
    {
        var source = ClientSource();

        source.Should().Contain("secondsListened <= 0", "a listen of nothing is not a play on either side");
        source.Should().Contain("secondsListened >= UNKNOWN_DURATION_SECONDS");
        source.Should().Contain("secondsListened >= LONG_LISTEN_SECONDS");
        source.Should().Contain("secondsListened / trackDurationSeconds >= MINIMUM_FRACTION");
    }
}
