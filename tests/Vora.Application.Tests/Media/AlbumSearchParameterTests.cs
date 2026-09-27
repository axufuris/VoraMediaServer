using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// The albums endpoint had no search term, so both native clients walked the whole
// library 200 rows at a time and filtered in memory — 11 requests on the first
// keystroke of a 2,197-album library. The term makes it one request.
//
// The regression that matters most is the absent term: it is every call that
// exists today, and it has to reach the repository as null rather than as an
// empty string, so "no search" stays one state rather than three.
public class AlbumSearchParameterTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    public AlbumSearchParameterTests()
    {
        _repository
            .GetAlbumsPageAsync(Arg.Any<Guid?>(), Arg.Any<MusicAccessFilter>(), Arg.Any<AlbumSortOrder>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns((new List<Album>(), 0));

        _manager = new MusicManager(
            _repository,
            Substitute.For<IUserRepository>(),
            Substitute.For<IUserMediaStateRepository>(),
            Array.Empty<IMusicArtworkProvider>(),
            Array.Empty<ILyricsProvider>(),
            Array.Empty<IListeningDataProvider>(),
            Substitute.For<IClientNotifier>(),
            Options.Create(new StoragePathsOptions()),
            new NullTaskProgressReporter(),
            NullLogger<MusicManager>.Instance);
    }

    private Task Albums(string? search, int offset = 0, int limit = 60) =>
        _manager.GetAlbumsAsync(null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, offset, limit, search);

    private Task Received(string? expectedTerm) =>
        _repository.Received(1).GetAlbumsPageAsync(
            Arg.Any<Guid?>(), Arg.Any<MusicAccessFilter>(), Arg.Any<AlbumSortOrder>(),
            Arg.Any<int>(), Arg.Any<int>(), expectedTerm);

    [Fact]
    public async Task A_term_reaches_the_query()
    {
        await Albums("zeppelin");

        await Received("zeppelin");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task No_term_reaches_the_query_as_null(string? search)
    {
        await Albums(search);

        await Received(null);
    }

    // Trimmed once, in the manager. A term that arrives padded must not reach the
    // query padded, or the pattern carries the spaces into the match.
    [Fact]
    public async Task A_padded_term_is_trimmed()
    {
        await Albums("  led zeppelin  ");

        await Received("led zeppelin");
    }

    // Wildcards survive the manager untouched; escaping is the query's job, and
    // doing it here as well would put the rule in two places.
    [Fact]
    public async Task Wildcards_are_passed_through_for_the_query_to_escape()
    {
        await Albums("50%");

        await Received("50%");
    }

    [Fact]
    public async Task Searching_does_not_change_how_the_page_is_clamped()
    {
        await Albums("zeppelin", offset: -5, limit: 5000);

        await _repository.Received(1).GetAlbumsPageAsync(
            Arg.Any<Guid?>(), Arg.Any<MusicAccessFilter>(), Arg.Any<AlbumSortOrder>(),
            0, MusicManager.MaxAlbumPageSize, "zeppelin");
    }

    [Fact]
    public async Task The_page_echoes_the_clamped_values_back_as_before()
    {
        var page = await _manager.GetAlbumsAsync(
            null, MusicAccessFilter.Unrestricted, AlbumSortOrder.RecentlyAdded, -5, 5000, "zeppelin");

        page.Offset.Should().Be(0);
        page.Limit.Should().Be(MusicManager.MaxAlbumPageSize);
    }
}
