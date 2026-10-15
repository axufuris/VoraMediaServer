using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Metadata;

public class HomeVideoMetadataTests
{
    private readonly IMetadataProvider _tmdb = Substitute.For<IMetadataProvider>();
    private readonly IMetadataProvider _local = Substitute.For<IMetadataProvider>();
    private readonly IArtworkProvider _tmdbArtwork = Substitute.For<IArtworkProvider>();
    private readonly IRatingsProvider _imdbRatings = Substitute.For<IRatingsProvider>();
    private readonly ILocalMediaReader _localMedia = Substitute.For<ILocalMediaReader>();
    private readonly MetadataFetchService _service;

    public HomeVideoMetadataTests()
    {
        _tmdb.Id.Returns("tmdb_metadata");
        _local.Id.Returns(LocalMetadataProvider.ProviderId);
        _tmdbArtwork.Id.Returns("tmdb_artwork");
        _imdbRatings.Id.Returns("omdb_imdb");
        _localMedia.FindArtworkAsync(Arg.Any<Guid>()).Returns([]);
        _service = new MetadataFetchService([_tmdb, _local], [_imdbRatings], [_tmdbArtwork], _localMedia);
    }

    private static Movie Video(LibraryType type) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Beach Day",
        ReleaseDate = new DateOnly(2019, 7, 4),
        Library = new MediaLibrary
        {
            Name = "Family",
            Type = type,
            MetadataProviderId = "tmdb_metadata",
            ArtworkProviderId = "tmdb_artwork",
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
    public async Task A_film_in_a_movie_library_is_still_looked_up()
    {
        await _service.GetTextMetadataAsync(Video(LibraryType.Movie));

        await _tmdb.Received(1).FetchMovieMetadataAsync("Beach Day", 2019, Arg.Any<CancellationToken>());
    }
}
