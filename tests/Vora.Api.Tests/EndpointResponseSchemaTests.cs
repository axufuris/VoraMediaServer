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
// Covers every endpoint file, with no exemption list. It was scoped to music
// first, while the other 26 routes across 13 files were still undeclared; an
// allow-list that long outlives its entries, which is the failure this kind of
// test exists to prevent, so the routes were fixed instead.
public class EndpointResponseSchemaTests
{
    private static IReadOnlyList<FileInfo> EndpointFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        dir.Should().NotBeNull("the test has to be able to find the source tree");

        var endpoints = new DirectoryInfo(Path.Combine(dir!.FullName, "src", "Vora.Api", "Endpoints"));
        endpoints.Exists.Should().BeTrue($"expected the endpoints at {endpoints.FullName}");

        var files = endpoints.GetFiles("*Endpoints.cs", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
        return files;
    }

    // Any receiver, not just one called `group`. Several files register on a
    // sub-group — `var authGroup = group.MapGroup("").RequireAuthorization();` —
    // and matching only `group.Map` made every route on those invisible, which is
    // how ArtworkEndpoints hid an anonymous response from an earlier pass.
    private static readonly Regex Route = new(
        @"\w+\.Map(?:Get|Post|Put|Delete|Patch)\(""(?<route>[^""]+)"",\s*(?<handler>\w+)\)(?<chain>[^;]*);",
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
    public void Every_route_that_returns_a_body_declares_its_schema()
    {
        var undeclared = new List<string>();

        foreach (var file in EndpointFiles())
        {
            var source = File.ReadAllText(file.FullName);
            var withBody = HandlersReturningABody(source);

            undeclared.AddRange(Route.Matches(source)
                .Where(m => withBody.Contains(m.Groups["handler"].Value))
                .Where(m => !m.Groups["chain"].Value.Contains(".Produces<", StringComparison.Ordinal))
                .Select(m => $"{file.Name}: {m.Groups["route"].Value} -> {m.Groups["handler"].Value}"));
        }

        undeclared.Should().BeEmpty(
            "a route with no Produces<> is a 200 with no schema in the OpenAPI document, so the generated "
            + "clients get a response they cannot read the body of. Add .Produces<T>(StatusCodes.Status200OK).");
    }

    // A schema of `object` is what an anonymous type produces, which is the same
    // failure wearing an annotation. The golden rule already says responses use a
    // VM or a Response class; this is what enforces it on the routes that matter.
    [Fact]
    public void No_route_answers_with_an_anonymous_type()
    {
        var offenders = EndpointFiles()
            .Where(f => File.ReadAllText(f.FullName).Contains("Results.Ok(new {", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        offenders.Should().BeEmpty(
            "an anonymous type serializes fine and describes nothing — give it a named response class");
    }

    // If the scanner stops recognising routes, both tests above pass by finding
    // nothing and prove only that they ran.
    [Fact]
    public void The_scanner_still_finds_the_routes_and_the_handlers()
    {
        var files = EndpointFiles();
        files.Should().HaveCountGreaterThan(15, "the api registers many endpoint groups");

        var music = File.ReadAllText(files.Single(f => f.Name == "MusicEndpoints.cs").FullName);

        Route.Matches(music).Should().HaveCountGreaterThan(40, "the music group registers many routes");
        HandlersReturningABody(music).Should().Contain("RecordTrackPlayAsync");
        HandlersReturningABody(music).Should().NotContain("UpdateNowPlayingAsync", "it returns an empty 200");
    }
}
