using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

// Fanart.tv is keyed by MusicBrainz id, so a provider must resolve one before it
// can ask for anything — and resolving means a search against an API that allows
// roughly a request a second. Without somewhere to keep the answer the cost
// scaled with the library on every refresh instead of once per artist ever, and
// the failures cascaded: no id means no Fanart call at all.
public class MusicBrainzIdCacheTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicBrainzIdCache _cache;

    private const string Mbid = "5b11f4ce-a62d-471e-81fc-a69a8278c7da";

    public MusicBrainzIdCacheTests()
    {
        _cache = new MusicBrainzIdCache(_repository, NullLogger<MusicBrainzIdCache>.Instance);
    }

    [Fact]
    public async Task Returns_a_stored_artist_id()
    {
        _repository.FindArtistByNameAsync("311").Returns(new Artist { Name = "311", MusicBrainzId = Mbid });

        var result = await _cache.GetArtistIdAsync("311", TestContext.Current.CancellationToken);

        result.Should().Be(Mbid);
    }

    // A miss has to read as "ask MusicBrainz", not as an empty id the provider
    // then sends to Fanart.
    [Fact]
    public async Task Reports_a_miss_when_the_artist_has_no_id_yet()
    {
        _repository.FindArtistByNameAsync("311").Returns(new Artist { Name = "311" });

        var result = await _cache.GetArtistIdAsync("311", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Reports_a_miss_for_an_artist_that_is_not_in_the_library()
    {
        _repository.FindArtistByNameAsync(Arg.Any<string>()).Returns((Artist?)null);

        var result = await _cache.GetArtistIdAsync("Nobody", TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Stores_a_resolved_artist_id()
    {
        var artist = new Artist { Name = "311" };
        _repository.FindArtistByNameAsync("311").Returns(artist);

        await _cache.SetArtistIdAsync("311", Mbid, TestContext.Current.CancellationToken);

        artist.MusicBrainzId.Should().Be(Mbid);
        await _repository.Received(1).UpdateArtistAsync(artist);
    }

    // Writing on every resolve would mean a database round trip per artwork
    // lookup even once the id is known.
    [Fact]
    public async Task Does_not_rewrite_an_unchanged_artist_id()
    {
        _repository.FindArtistByNameAsync("311").Returns(new Artist { Name = "311", MusicBrainzId = Mbid });

        await _cache.SetArtistIdAsync("311", Mbid, TestContext.Current.CancellationToken);

        await _repository.DidNotReceive().UpdateArtistAsync(Arg.Any<Artist>());
    }

    // An artist the scanner has not created yet simply has nowhere to keep the
    // id. That is not an error — the provider still works, it just pays again.
    [Fact]
    public async Task Storing_an_id_for_an_unknown_artist_is_a_no_op()
    {
        _repository.FindArtistByNameAsync(Arg.Any<string>()).Returns((Artist?)null);

        var act = async () => await _cache.SetArtistIdAsync("Nobody", Mbid, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        await _repository.DidNotReceive().UpdateArtistAsync(Arg.Any<Artist>());
    }

    [Fact]
    public async Task Returns_and_stores_an_album_release_group_id()
    {
        var album = new Album { Title = "Grassroots", ArtistId = Guid.NewGuid() };
        _repository.FindAlbumByArtistAndTitleAsync("311", "Grassroots").Returns(album);

        (await _cache.GetAlbumIdAsync("311", "Grassroots", TestContext.Current.CancellationToken)).Should().BeNull();

        await _cache.SetAlbumIdAsync("311", "Grassroots", Mbid, TestContext.Current.CancellationToken);

        album.MusicBrainzId.Should().Be(Mbid);
        await _repository.Received(1).UpdateAlbumAsync(album);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ignores_blank_names(string name)
    {
        (await _cache.GetArtistIdAsync(name, TestContext.Current.CancellationToken)).Should().BeNull();
        await _repository.DidNotReceive().FindArtistByNameAsync(Arg.Any<string>());
    }
}
