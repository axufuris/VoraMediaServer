using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class AlbumCoverArtistFallbackTests
{
    private readonly IMusicRepository _repository = Substitute.For<IMusicRepository>();
    private readonly MusicManager _manager;

    private readonly Artist _artist = new() { Id = Guid.NewGuid(), Name = "3 Doors Down", ArtworkUrl = ArtistThumb };

    private const string ArtistThumb = "https://r2.theaudiodb.com/images/media/artist/thumb/band.jpg";
    private const string AlbumCover = "https://coverartarchive.org/release/abc/front.jpg";

    public AlbumCoverArtistFallbackTests()
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
    }

    private Album AlbumWith(string? artworkUrl) => new()
    {
        Id = Guid.NewGuid(),
        Title = artworkUrl == null ? "Duck and Run" : "Away from the Sun",
        ArtistId = _artist.Id,
        Artist = _artist,
        ArtworkUrl = artworkUrl
    };

    private static Track TrackOn(Album album) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Track on " + album.Title,
        AlbumId = album.Id,
        Album = album
    };

    [Fact]
    public async Task A_popular_track_whose_album_has_no_cover_shows_the_artists_image()
    {
        var bare = TrackOn(AlbumWith(null));
        var covered = TrackOn(AlbumWith(AlbumCover));
        _repository.GetTopTracksForArtistAsync(_artist.Id, Arg.Any<MusicAccessFilter>(), 10)
            .Returns(new List<Track> { bare, covered });

        var tracks = await _manager.GetTopTracksForArtistAsync(_artist.Id, null, MusicAccessFilter.Unrestricted, 10);

        tracks.Single(t => t.Id == bare.Id).AlbumArtworkUrl.Should().Be(ArtistThumb);
        tracks.Single(t => t.Id == covered.Id).AlbumArtworkUrl.Should().Be(AlbumCover);
    }

    [Fact]
    public async Task An_album_tile_keeps_its_own_artwork_empty_and_carries_the_artists_separately()
    {
        var album = AlbumWith(null);
        _repository.GetArtistByIdAsync(_artist.Id, Arg.Any<MusicAccessFilter>()).Returns(_artist);
        _repository.GetAlbumsForArtistAsync(_artist.Id, Arg.Any<MusicAccessFilter>()).Returns(new List<Album> { album });

        var albums = (await _manager.GetArtistDetailAsync(_artist.Id, null, MusicAccessFilter.Unrestricted))!.Albums;

        albums.Single().ArtworkUrl.Should().BeNull();
        albums.Single().ArtistArtworkUrl.Should().Be(ArtistThumb);
    }

    [Fact]
    public async Task Saving_the_edit_modal_on_an_album_without_cover_does_not_persist_the_artists_image()
    {
        var album = AlbumWith(null);
        _repository.GetAlbumByIdAsync(album.Id, Arg.Any<MusicAccessFilter>()).Returns(album);
        _repository.GetTracksForAlbumAsync(album.Id, Arg.Any<MusicAccessFilter>()).Returns(new List<Track>());
        _repository.GetAlbumForUpdateAsync(album.Id).Returns(album);

        var detail = await _manager.GetAlbumDetailAsync(album.Id, null, MusicAccessFilter.Unrestricted);
        var albumVm = detail.Album ?? throw new InvalidOperationException("album detail missing");
        albumVm.ArtistArtworkUrl.Should().Be(ArtistThumb);

        await _manager.UpdateAlbumAsync(album.Id, new UpdateAlbumRequest
        {
            Title = albumVm.Title,
            Year = 2008,
            ArtworkUrl = albumVm.ArtworkUrl,
            BackgroundUrl = albumVm.BackgroundUrl,
            DiscArtUrl = albumVm.DiscArtUrl
        });

        album.ArtworkUrl.Should().BeNull();
        album.Year.Should().Be(2008);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_album_cover_counts_as_missing(string? albumArtwork)
    {
        AlbumCoverArt.Resolve(albumArtwork, ArtistThumb).Should().Be(ArtistThumb);
    }

    [Fact]
    public void Neither_having_an_image_resolves_to_null()
    {
        AlbumCoverArt.Resolve(null, " ").Should().BeNull();
        AlbumCoverArt.For(null).Should().BeNull();
    }

    [Fact]
    public void A_mix_cover_prefers_real_album_art_over_the_artist_fallback()
    {
        var tracks = new List<Track> { TrackOn(AlbumWith(null)), TrackOn(AlbumWith(AlbumCover)) };

        AlbumCoverArt.FirstFor(tracks).Should().Be(AlbumCover);
        AlbumCoverArt.FirstFor(tracks.Take(1)).Should().Be(ArtistThumb);
    }
}
