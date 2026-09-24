using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

// Most albums have no background: fanart's album coverage is thin and embedded
// cover art never carries one, while the artist almost always does. So the album
// page rendered flat black with a fully dressed artist page one tap away.
//
// The fallback is deliberately a SEPARATE field on the detail response rather
// than AlbumVM.BackgroundUrl. The edit modal seeds its form from
// album.backgroundUrl and UpdateAlbumRequest writes it straight back, so
// substituting the artist's url there would stamp it onto the album row the
// first time an admin opened the modal to change a year — a display fallback
// quietly becoming the album's own data, after which it stops following the
// artist and nothing reports it.
public class AlbumArtistBackgroundFallbackTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    private readonly Guid _albumId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    private const string ArtistBackground = "https://r2.theaudiodb.com/images/media/artist/fanart/band.jpg";
    private const string AlbumBackground = "https://assets.fanart.tv/fanart/music/abc/albumbackground/own.jpg";

    public AlbumArtistBackgroundFallbackTests()
    {
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

        _repository.GetTracksForAlbumAsync(_albumId, Arg.Any<MusicAccessFilter>()).Returns(new List<Track>());
    }

    private Album GivenAlbum(string? albumBackground, Guid? artistId = null)
    {
        var album = new Album
        {
            Id = _albumId,
            Title = "Dammit!",
            ArtistId = artistId ?? _artistId,
            BackgroundUrl = albumBackground
        };
        _repository.GetAlbumByIdAsync(_albumId, Arg.Any<MusicAccessFilter>()).Returns(album);
        return album;
    }

    private void GivenArtistIsVisible(string? background) =>
        _repository.GetArtistByIdAsync(_artistId, Arg.Any<MusicAccessFilter>())
            .Returns(new Artist { Id = _artistId, Name = "311", BackgroundUrl = background });

    private void GivenArtistIsNotVisible() =>
        _repository.GetArtistByIdAsync(_artistId, Arg.Any<MusicAccessFilter>()).Returns((Artist?)null);

    private Task<(AlbumVM? Album, List<TrackVM> Tracks, string? ArtistBackgroundUrl)> Detail() =>
        _manager.GetAlbumDetailAsync(_albumId, null, MusicAccessFilter.Unrestricted);

    [Fact]
    public async Task An_album_with_no_background_borrows_the_artists()
    {
        GivenAlbum(null);
        GivenArtistIsVisible(ArtistBackground);

        var (album, _, artistBackgroundUrl) = await Detail();

        artistBackgroundUrl.Should().Be(ArtistBackground);
        album!.BackgroundUrl.Should().BeNull("the album's own value is untouched, including when it is null");
    }

    // The common path must cost nothing extra, and there must be no ambiguity
    // about which image won.
    [Fact]
    public async Task An_album_with_its_own_background_is_unchanged_and_asks_for_no_artist()
    {
        GivenAlbum(AlbumBackground);
        GivenArtistIsVisible(ArtistBackground);

        var (album, _, artistBackgroundUrl) = await Detail();

        album!.BackgroundUrl.Should().Be(AlbumBackground);
        artistBackgroundUrl.Should().BeNull();
        await _repository.DidNotReceive().GetArtistByIdAsync(Arg.Any<Guid>(), Arg.Any<MusicAccessFilter>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_background_counts_as_having_none(string albumBackground)
    {
        GivenAlbum(albumBackground);
        GivenArtistIsVisible(ArtistBackground);

        var (_, _, artistBackgroundUrl) = await Detail();

        artistBackgroundUrl.Should().Be(ArtistBackground);
    }

    [Fact]
    public async Task Neither_having_one_leaves_the_page_flat_as_it_is_today()
    {
        GivenAlbum(null);
        GivenArtistIsVisible(null);

        var (album, _, artistBackgroundUrl) = await Detail();

        album!.BackgroundUrl.Should().BeNull();
        artistBackgroundUrl.Should().BeNull();
    }

    // The lookup goes through the access filter, so this is the repository
    // answering null rather than the manager choosing to hide it.
    [Fact]
    public async Task A_profile_that_cannot_see_the_artist_gets_null_not_the_url()
    {
        GivenAlbum(null);
        GivenArtistIsNotVisible();

        var (_, _, artistBackgroundUrl) = await Detail();

        artistBackgroundUrl.Should().BeNull();
    }

    // A compilation points at a "Various Artists" row, which is an artist like any
    // other — whatever background it has, or null.
    [Fact]
    public async Task A_compilation_follows_its_various_artists_row()
    {
        GivenAlbum(null);
        GivenArtistIsVisible(ArtistBackground);

        var (_, _, artistBackgroundUrl) = await Detail();

        artistBackgroundUrl.Should().Be(ArtistBackground);
    }

    [Fact]
    public async Task An_album_with_no_artist_at_all_is_not_looked_up()
    {
        GivenAlbum(null, artistId: Guid.Empty);

        var (_, _, artistBackgroundUrl) = await Detail();

        artistBackgroundUrl.Should().BeNull();
        await _repository.DidNotReceive().GetArtistByIdAsync(Arg.Any<Guid>(), Arg.Any<MusicAccessFilter>());
    }

    // THE regression this whole design exists to prevent. The edit modal seeds
    // its form from album.backgroundUrl and posts it back, so an admin opening
    // the modal on a borrowing album and saving anything at all must not persist
    // the artist's url onto the album row.
    [Fact]
    public async Task Saving_the_edit_modal_on_a_borrowing_album_does_not_persist_the_artists_url()
    {
        var album = GivenAlbum(null);
        GivenArtistIsVisible(ArtistBackground);
        _repository.GetAlbumForUpdateAsync(_albumId).Returns(album);

        var (albumVm, _, artistBackgroundUrl) = await Detail();
        artistBackgroundUrl.Should().Be(ArtistBackground, "the page is dressed from the artist");

        // What the modal posts: the form seeded from the album's own value, with
        // an unrelated field edited.
        await _manager.UpdateAlbumAsync(_albumId, new UpdateAlbumRequest
        {
            Title = albumVm!.Title,
            Year = 1990,
            BackgroundUrl = albumVm.BackgroundUrl,
            ArtworkUrl = albumVm.ArtworkUrl,
            DiscArtUrl = albumVm.DiscArtUrl
        });

        album.BackgroundUrl.Should().BeNull("the album must keep following its artist, not freeze a copy of the url");
        album.Year.Should().Be(1990, "the edit the admin actually made still lands");
    }
}
