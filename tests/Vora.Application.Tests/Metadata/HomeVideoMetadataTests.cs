using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Application.Tests.Metadata;

public class HomeVideoMetadataTests
{
    private readonly IMetadataProvider _tmdb = Substitute.For<IMetadataProvider>();
    private readonly IMetadataProvider _local = Substitute.For<IMetadataProvider>();
    private readonly IArtworkProvider _tmdbArtwork = Substitute.For<IArtworkProvider>();
    private readonly IRatingsProvider _imdbRatings = Substitute.For<IRatingsProvider>();
    private readonly ILocalMediaReader _localMedia = Substitute.For<ILocalMediaReader>();
    private readonly IVideoFrameArtworkService _frames = Substitute.For<IVideoFrameArtworkService>();
    private readonly MetadataFetchService _service;

    public HomeVideoMetadataTests()
    {
        _tmdb.Id.Returns("tmdb_metadata");
        _local.Id.Returns(LocalMetadataProvider.ProviderId);
        _tmdbArtwork.Id.Returns("tmdb_artwork");
        _imdbRatings.Id.Returns("omdb_imdb");
        _localMedia.FindArtworkAsync(Arg.Any<Guid>()).Returns([]);
        _frames.GetFrameArtworkAsync(Arg.Any<MediaItem>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns([]);
        _service = new MetadataFetchService([_tmdb, _local], [_imdbRatings], [_tmdbArtwork], _localMedia, _frames);
    }

    private static Movie Video(LibraryType type, string artworkProviderId = "tmdb_artwork") => new()
    {
        Id = Guid.NewGuid(),
        Title = "Beach Day",
        ReleaseDate = new DateOnly(2019, 7, 4),
        Library = new MediaLibrary
        {
            Name = "Family",
            Type = type,
            MetadataProviderId = "tmdb_metadata",
            ArtworkProviderId = artworkProviderId,
            ThirdPartyRating1ProviderId = "omdb_imdb",
        },
    };

    [Fact]
    public async Task A_home_video_is_never_looked_up_online_whatever_its_library_names()
    {
        var video = Video(LibraryType.HomeVideo);

        var (_, providerId, _) = await _service.GetTextMetadataAsync(video);
        var (ratings, _) = await _service.GetSecondaryDataAsync(video, forceOverride: true, TestContext.Current.CancellationToken);

        providerId.Should().Be(LocalMetadataProvider.ProviderId);
        ratings.Rating1.Should().BeNull();
        await _tmdb.DidNotReceive().FetchMovieMetadataAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _tmdbArtwork.DidNotReceive().GetArtworkAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _imdbRatings.DidNotReceive().FetchRatingAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _localMedia.Received(1).FindArtworkAsync(video.Id);
    }

    [Fact]
    public async Task A_home_video_with_nothing_beside_it_gets_a_poster_and_backdrop_from_its_own_picture()
    {
        var video = Video(LibraryType.HomeVideo);
        var frame = new MediaArtwork { Url = "/api/artwork/custom/frame_poster.jpg", Kind = ArtworkKind.Poster, ProviderId = VideoFrameArtworkService.ProviderId };
        _frames.GetFrameArtworkAsync(video, true, true, Arg.Any<CancellationToken>()).Returns([frame]);

        var artwork = await _service.GetArtworkAsync(video);

        artwork.Should().ContainSingle().Which.Should().BeSameAs(frame);
    }

    [Fact]
    public async Task A_poster_beside_a_home_video_wins_and_only_the_backdrop_comes_from_the_video()
    {
        var video = Video(LibraryType.HomeVideo);
        _localMedia.FindArtworkAsync(video.Id).Returns([new MediaArtwork { Url = "/local/poster.jpg", Kind = ArtworkKind.Poster, ProviderId = LocalArtworkProvider.ProviderId }]);

        await _service.GetArtworkAsync(video);

        await _frames.Received(1).GetFrameArtworkAsync(video, false, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_film_never_gets_a_poster_from_its_own_picture()
    {
        await _service.GetArtworkAsync(Video(LibraryType.Movie, LocalArtworkProvider.ProviderId));

        await _frames.DidNotReceive().GetFrameArtworkAsync(Arg.Any<MediaItem>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_film_in_a_movie_library_is_still_looked_up()
    {
        await _service.GetTextMetadataAsync(Video(LibraryType.Movie));

        await _tmdb.Received(1).FetchMovieMetadataAsync("Beach Day", 2019, Arg.Any<CancellationToken>());
    }
}
