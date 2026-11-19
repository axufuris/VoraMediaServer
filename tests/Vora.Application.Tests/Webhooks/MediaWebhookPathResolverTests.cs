using Vora.Application.Webhooks;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Webhooks;

public class MediaWebhookPathResolverTests
{
    private static readonly string Movies = Path.Combine(Path.GetTempPath(), "vora-resolver", "movies");
    private static readonly string Tv = Path.Combine(Path.GetTempPath(), "vora-resolver", "tv");

    private static readonly MediaWebhookLibrary MovieLibrary = new(Guid.NewGuid(), "Movies", LibraryType.Movie, new List<string> { Movies }, new List<string>());
    private static readonly MediaWebhookLibrary TvLibrary = new(Guid.NewGuid(), "TV", LibraryType.TvShow, new List<string> { Tv }, new List<string>());

    private static MediaWebhookPathResolver Resolver(string mappings, Func<string, bool>? anchorExists = null, params MediaWebhookLibrary[] libraries) =>
        new(
            MediaWebhookPathMappings.Parse(mappings),
            libraries.Length == 0 ? new[] { MovieLibrary, TvLibrary } : libraries,
            top => Task.FromResult(anchorExists?.Invoke(top) ?? false));

    [Fact]
    public void Mappings_are_one_per_line_and_bad_lines_are_dropped()
    {
        var mappings = MediaWebhookPathMappings.Parse("/tv => /media/tv\r\n\r\nnonsense\n=> /x\n/y =>\n C:\\Media\\Movies=>/media/movies ");

        mappings.Should().Equal(new MediaWebhookPathMapping("/tv", "/media/tv"), new MediaWebhookPathMapping("C:\\Media\\Movies", "/media/movies"));
        MediaWebhookPathMappings.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public async Task A_mapping_puts_the_file_in_the_library_folder_it_names()
    {
        var resolution = await Resolver($"/data/tv => {Tv}").ResolveAsync("/data/tv/Show/Season 1/e1.mkv");

        resolution.Target.Should().NotBeNull();
        resolution.Target!.Library.Should().Be(TvLibrary);
        resolution.Target.Root.Should().Be(Tv);
        resolution.Target.Path.Should().Be(Path.Combine(Tv, "Show", "Season 1", "e1.mkv"));
    }

    [Fact]
    public async Task The_longest_matching_mapping_wins()
    {
        var other = Path.Combine(Path.GetTempPath(), "vora-resolver", "tv-kids");
        var kids = new MediaWebhookLibrary(Guid.NewGuid(), "Kids", LibraryType.TvShow, new List<string> { other }, new List<string>());

        var resolution = await Resolver($"/data => {Tv}\n/data/kids => {other}", null, TvLibrary, kids).ResolveAsync("/data/kids/Bluey/S1/e1.mkv");

        resolution.Target!.Library.Should().Be(kids);
        resolution.Target.Path.Should().Be(Path.Combine(other, "Bluey", "S1", "e1.mkv"));
    }

    [Fact]
    public async Task A_windows_path_maps_through_a_windows_mapping()
    {
        var resolution = await Resolver($"D:\\Media\\Movies => {Movies}").ResolveAsync("D:\\Media\\Movies\\Film (2020)\\Film.mkv");

        resolution.Target!.Path.Should().Be(Path.Combine(Movies, "Film (2020)", "Film.mkv"));
    }

    [Fact]
    public async Task A_mapping_to_somewhere_that_is_not_a_library_says_so()
    {
        var resolution = await Resolver("/tv => /somewhere/else").ResolveAsync("/tv/Show/e1.mkv");

        resolution.Target.Should().BeNull();
        resolution.Reason.Should().Contain("/tv => /somewhere/else").And.Contain("not inside any library folder");
    }

    [Fact]
    public async Task A_path_already_inside_a_library_folder_needs_no_mapping()
    {
        var path = Path.Combine(Movies, "Film (2020)", "Film.mkv");

        var resolution = await Resolver(string.Empty).ResolveAsync(path);

        resolution.Target!.Library.Should().Be(MovieLibrary);
        resolution.Target.Path.Should().Be(path);
    }

    [Fact]
    public async Task With_no_mapping_the_leading_folders_are_stripped_until_a_folder_that_exists_under_a_library()
    {
        var resolution = await Resolver(
            string.Empty,
            top => top == Path.Combine(Tv, "Show")).ResolveAsync("/data/media/tv/Show/Season 1/e1.mkv");

        resolution.Target!.Library.Should().Be(TvLibrary);
        resolution.Target.Path.Should().Be(Path.Combine(Tv, "Show", "Season 1", "e1.mkv"));
    }

    [Fact]
    public async Task Stripping_takes_as_little_as_it_can()
    {
        var resolution = await Resolver(
            string.Empty,
            top => top == Path.Combine(Tv, "Show") || top == Path.Combine(Tv, "Season 1")).ResolveAsync("/tv/Show/Season 1/e1.mkv");

        resolution.Target!.Path.Should().Be(Path.Combine(Tv, "Show", "Season 1", "e1.mkv"));
    }

    [Fact]
    public async Task With_nothing_to_anchor_on_it_asks_for_a_mapping()
    {
        var resolution = await Resolver(string.Empty).ResolveAsync("/tv/Unknown Show/e1.mkv");

        resolution.Target.Should().BeNull();
        resolution.Reason.Should().Contain("path mapping");
    }

    [Fact]
    public async Task Only_the_libraries_it_was_given_are_considered()
    {
        var resolution = await Resolver($"/tv => {Tv}", null, MovieLibrary).ResolveAsync("/tv/Show/e1.mkv");

        resolution.Target.Should().BeNull();
    }

    [Fact]
    public async Task A_path_that_climbs_out_of_its_folder_is_refused_but_dots_in_a_name_are_fine()
    {
        (await Resolver($"/tv => {Tv}").ResolveAsync("/tv/../etc/passwd")).Target.Should().BeNull();
        (await Resolver($"/tv => {Tv}").ResolveAsync("/tv/Wait... What (2020)/Wait... What.mkv")).Target!.Path
            .Should().Be(Path.Combine(Tv, "Wait... What (2020)", "Wait... What.mkv"));
        (await Resolver(string.Empty).ResolveAsync(string.Empty)).Target.Should().BeNull();
    }

    [Fact]
    public void A_mapping_line_without_an_arrow_or_a_side_is_found()
    {
        MediaWebhookPathMappings.FirstBadLine(null).Should().BeNull();
        MediaWebhookPathMappings.FirstBadLine("/tv => /media/tv\n\n/movies => /media/movies").Should().BeNull();
        MediaWebhookPathMappings.FirstBadLine("/tv => /media/tv\n/movies /media/movies").Should().Be("/movies /media/movies");
        MediaWebhookPathMappings.FirstBadLine("=> /media/tv").Should().Be("=> /media/tv");
        MediaWebhookPathMappings.FirstBadLine("/tv =>").Should().Be("/tv =>");
    }

    [Fact]
    public void Root_folders_are_matched_to_library_folders_by_their_last_name_or_the_only_one()
    {
        var byName = MediaWebhookMappingSuggester.Suggest(new[] { "/data/Movies/", "/data/anime" }, new[] { "/media/anime", "/media/movies" });
        byName.Mappings.Should().Be("/data/Movies => /media/movies\n/data/anime => /media/anime");
        byName.Unmatched.Should().BeEmpty();

        var onlyOne = MediaWebhookMappingSuggester.Suggest(new[] { "/data/films" }, new[] { "/media/movies" });
        onlyOne.Mappings.Should().Be("/data/films => /media/movies");

        var unmatched = MediaWebhookMappingSuggester.Suggest(new[] { "/data/films", "/data/kids" }, new[] { "/media/movies", "/media/tv" });
        unmatched.Mappings.Should().BeEmpty();
        unmatched.Unmatched.Should().Equal("/data/films", "/data/kids");

        MediaWebhookMappingSuggester.Suggest(new[] { "", "  " }, new[] { "/media/movies" }).Mappings.Should().BeEmpty();
        MediaWebhookMappingSuggester.Suggest(new[] { "/data/films" }, Array.Empty<string>()).Unmatched.Should().Equal("/data/films");
    }


    [Fact]
    public void A_root_folder_is_matched_to_the_folder_above_library_folders_that_share_its_name()
    {
        var movies = new[] { "/media/movies/1080p", "/media/movies/4K", "/media/movies/Test/1080p" };
        var shows = new[] { "/mnt/user/Media/TV/1080p", "/mnt/user/Media/TV/4K" };

        MediaWebhookMappingSuggester.Suggest(new[] { "/movies" }, movies).Mappings.Should().Be("/movies => /media/movies");
        MediaWebhookMappingSuggester.Suggest(new[] { "/tv" }, shows).Mappings.Should().Be("/tv => /mnt/user/Media/TV");
        MediaWebhookMappingSuggester.Suggest(new[] { "/movies" }, movies).Unmatched.Should().BeEmpty();
    }

    [Fact]
    public void The_candidate_that_shares_the_most_of_the_ending_wins_and_a_library_folder_wins_a_tie()
    {
        var movies = new[] { "/media/movies/1080p", "/media/movies/4K", "/media/movies/Test/1080p" };

        MediaWebhookMappingSuggester.Suggest(new[] { "/movies/1080p" }, movies).Mappings.Should().Be("/movies/1080p => /media/movies/1080p");
        MediaWebhookMappingSuggester.Suggest(new[] { "/data/4k" }, movies).Mappings.Should().Be("/data/4k => /media/movies/4K");
        MediaWebhookMappingSuggester.Suggest(new[] { "/1080p" }, movies).Mappings.Should().Be("/1080p => /media/movies/1080p");
    }

    [Fact]
    public void A_root_that_shares_nothing_with_several_library_folders_is_left_unmatched()
    {
        var movies = new[] { "/media/movies/1080p", "/media/movies/4K" };

        var suggestion = MediaWebhookMappingSuggester.Suggest(new[] { "/data/films", "/movies" }, movies);

        suggestion.Mappings.Should().Be("/movies => /media/movies");
        suggestion.Unmatched.Should().Equal("/data/films");
    }

}
