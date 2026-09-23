using System.Text.RegularExpressions;

namespace Vora.Api.Tests;

// A route that answers with a body and carries no Produces<> annotation is
// described in the OpenAPI document as a 200 with no schema. It works perfectly
// in the browser and fails silently everywhere the document is the contract: the
// generated Swift and Kotlin clients hand back a response with nothing to read,
// so the body has to be hand-written on the other side or quietly dropped.
//
// That is exactly what happened to POST /tracks/{id}/played. It was changed to
// return { recorded, outcome } — the value the whole play-recording contract asks
// clients to check — without an annotation, and the client author had to write
// the model by hand to get at it.
//
// Scoped to the music endpoints because that is the surface the native clients
// are being generated from today. The same check over every endpoint file finds
// 26 more routes in 13 files; widening it is the follow-up, and it needs those
// fixed first rather than an exemption list that would outlive them.
public class MusicEndpointResponseSchemaTests
{
    private static string EndpointSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        dir.Should().NotBeNull("the test has to be able to find the source tree");

        var path = Path.Combine(dir!.FullName, "src", "Vora.Api", "Endpoints", "MusicEndpoints.cs");
        File.Exists(path).Should().BeTrue($"expected the music endpoints at {path}");
        return File.ReadAllText(path);
    }

    private static readonly Regex Route = new(
        @"group\.Map(?:Get|Post|Put|Delete|Patch)\(""(?<route>[^""]+)"",\s*(?<handler>\w+)\)(?<chain>[^;]*);",
        RegexOptions.Compiled);

    // Task<IResult> and bare IResult both: a synchronous handler is still a
    // handler, and missing one makes the PREVIOUS handler's slice run into its
    // body and inherit its return.
    private static readonly Regex Handler = new(
        @"(?:private|public) static (?:async )?(?:Task<IResult>|IResult) (?<name>\w+)\(",
        RegexOptions.Compiled);

    // Results.Ok(x), not Results.Ok(). The latter is a genuine empty 200 and has
    // nothing to describe.
    private static readonly Regex ReturnsBody = new(@"Results\.Ok\(\s*[^)\s]", RegexOptions.Compiled);

    private static HashSet<string> HandlersReturningABody(string source)
    {
        var matches = Handler.Matches(source).ToList();
        var names = new HashSet<string>();

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
            if (ReturnsBody.IsMatch(source[start..end])) names.Add(matches[i].Groups["name"].Value);
        }

        return names;
    }

    [Fact]
    public void Every_music_route_that_returns_a_body_declares_its_schema()
    {
        var source = EndpointSource();
        var withBody = HandlersReturningABody(source);

        var undeclared = Route.Matches(source)
            .Where(m => withBody.Contains(m.Groups["handler"].Value))
            .Where(m => !m.Groups["chain"].Value.Contains(".Produces<", StringComparison.Ordinal))
            .Select(m => $"{m.Groups["route"].Value} -> {m.Groups["handler"].Value}")
            .ToList();

        undeclared.Should().BeEmpty(
            "a route with no Produces<> is a 200 with no schema in the OpenAPI document, so the generated "
            + "clients get a response they cannot read the body of. Add .Produces<T>(StatusCodes.Status200OK).");
    }

    // A schema of `object` is what an anonymous type produces, which is the same
    // failure wearing an annotation. The golden rule already says responses use a
    // VM or a Response class; this is what enforces it on the routes that matter.
    [Fact]
    public void No_music_route_answers_with_an_anonymous_type()
    {
        EndpointSource().Should().NotContain("Results.Ok(new {",
            "an anonymous type serializes fine and describes nothing — give it a named response class");
    }

    // If the scanner stops recognising routes, both tests above pass by finding
    // nothing and prove only that they ran.
    [Fact]
    public void The_scanner_still_finds_the_routes_and_the_handlers()
    {
        var source = EndpointSource();

        Route.Matches(source).Should().HaveCountGreaterThan(40, "the music group registers many routes");
        HandlersReturningABody(source).Should().Contain("RecordTrackPlayAsync");
        HandlersReturningABody(source).Should().NotContain("UpdateNowPlayingAsync", "it returns an empty 200");
    }
}
