using Microsoft.Extensions.Options;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Application.Tests.Metadata;

public class LocalArtworkSelectionTests
{
    private const string OnlinePoster = "https://image.tmdb.org/t/p/w500/online.jpg";
    private const string OnlineBackdrop = "https://image.tmdb.org/t/p/w1280/online.jpg";

    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly IMediaArtworkRepository _artwork = Substitute.For<IMediaArtworkRepository>();
    private readonly MetadataMappingService _mapper;

    public LocalArtworkSelectionTests()
    {
        var pluginSettings = Substitute.For<IPluginSettingsProvider>();
        pluginSettings.GetMetadataLanguageAsync().Returns("eng");
        _mapper = new MetadataMappingService(_repository, _artwork, Substitute.For<IActorRepository>(), Substitute.For<ICollectionRepository>(),
            Substitute.For<IReferenceRepository>(), new ReferenceWriteGate(), Options.Create(new StoragePathsOptions()), pluginSettings);
    }

    private static Movie Movie(bool preferLocalArtwork) => new()
    {
        Title = "Alien",
        Library = new MediaLibrary { Name = "Movies", PreferLocalArtwork = preferLocalArtwork }
    };

    private static string Local(MediaItem item, string fileName) => $"{LocalArtworkAddress.Prefix}{item.Id}/{fileName}?v=1";

    private static List<MediaArtwork> Found(MediaItem item) =>
    [
        new() { MediaItemId = item.Id, Kind = ArtworkKind.Poster, Url = OnlinePoster, VoteAverage = 10, ProviderId = "tmdb_artwork" },
        new() { MediaItemId = item.Id, Kind = ArtworkKind.Backdrop, Url = OnlineBackdrop, VoteAverage = 10, ProviderId = "tmdb_artwork" },
        new() { MediaItemId = item.Id, Kind = ArtworkKind.Logo, Url = "https://image.tmdb.org/t/p/w500/logo.png", Language = "en", VoteAverage = 10, ProviderId = "tmdb_artwork" },
        new() { MediaItemId = item.Id, Kind = ArtworkKind.Poster, Url = Local(item, "poster.jpg"), Language = "Local", ProviderId = LocalArtworkProvider.ProviderId },
        new() { MediaItemId = item.Id, Kind = ArtworkKind.Logo, Url = Local(item, "logo.png"), Language = "Local", ProviderId = LocalArtworkProvider.ProviderId }
    ];

    [Fact]
    public async Task A_library_that_prefers_local_images_uses_them_and_fills_the_rest_from_the_provider()
    {
        var movie = Movie(preferLocalArtwork: true);

        await _mapper.ApplyArtworkAsync(movie, Found(movie), forceOverride: false);

        movie.PosterUrl.Should().Be(Local(movie, "poster.jpg"));
        movie.OriginalPosterUrl.Should().Be(Local(movie, "poster.jpg"));
        movie.LogoUrl.Should().Be(Local(movie, "logo.png"));
        movie.BackgroundUrl.Should().Be(OnlineBackdrop);
    }

    [Fact]
    public async Task A_library_that_does_not_prefer_local_images_keeps_choosing_the_best_rated()
    {
        var movie = Movie(preferLocalArtwork: false);

        await _mapper.ApplyArtworkAsync(movie, Found(movie), forceOverride: false);

        movie.PosterUrl.Should().Be(OnlinePoster);
        movie.LogoUrl.Should().Be("https://image.tmdb.org/t/p/w500/logo.png");
    }

    [Fact]
    public async Task Refreshing_artwork_replaces_what_providers_found_and_keeps_what_the_admin_uploaded()
    {
        var movie = Movie(preferLocalArtwork: true);
        var found = Found(movie);

        await _mapper.ApplyArtworkAsync(movie, found, forceOverride: false);

        await _artwork.Received(1).ReplaceProviderMediaArtworkAsync(movie.Id, found);
        await _artwork.DidNotReceive().ReplaceMediaArtworkAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<MediaArtwork>>());
    }

    [Fact]
    public async Task The_poster_that_comes_with_the_providers_details_does_not_push_a_local_one_out()
    {
        var movie = Movie(preferLocalArtwork: true);
        movie.PosterUrl = movie.OriginalPosterUrl = Local(movie, "poster.jpg");
        movie.BackgroundUrl = Local(movie, "fanart.jpg");

        await _mapper.ApplyTextMetadataAsync(movie, new MetadataResult { Title = "Alien", PosterUrl = OnlinePoster, BackgroundUrl = OnlineBackdrop }, false, "tmdb_metadata", "TMDB");

        movie.PosterUrl.Should().Be(Local(movie, "poster.jpg"));
        movie.BackgroundUrl.Should().Be(Local(movie, "fanart.jpg"));
    }

    [Fact]
    public async Task Without_the_preference_the_providers_poster_replaces_a_local_one_as_before()
    {
        var movie = Movie(preferLocalArtwork: false);
        movie.PosterUrl = movie.OriginalPosterUrl = Local(movie, "poster.jpg");

        await _mapper.ApplyTextMetadataAsync(movie, new MetadataResult { Title = "Alien", PosterUrl = OnlinePoster }, false, "tmdb_metadata", "TMDB");

        movie.PosterUrl.Should().Be(OnlinePoster);
    }

    [Fact]
    public async Task A_shows_refresh_leaves_a_seasons_local_poster_in_place()
    {
        var library = new MediaLibrary { Name = "TV", PreferLocalArtwork = true };
        var show = new TvShow { Title = "Show", Library = library };
        var local = new Season { Title = "Season 1", SeasonNumber = 1, TvShow = show, Library = library };
        local.PosterUrl = local.OriginalPosterUrl = Local(local, "season01-poster.jpg");
        var online = new Season { Title = "Season 2", SeasonNumber = 2, TvShow = show, Library = library };
        show.Seasons.Add(local);
        show.Seasons.Add(online);

        await _mapper.ApplyTextMetadataAsync(show, new MetadataResult
        {
            Title = "Show",
            Seasons =
            [
                new SeasonResult { Id = 11, SeasonNumber = 1, PosterUrl = "https://image.tmdb.org/t/p/w500/s1.jpg" },
                new SeasonResult { Id = 12, SeasonNumber = 2, PosterUrl = "https://image.tmdb.org/t/p/w500/s2.jpg" }
            ]
        }, false, "tmdb_metadata", "TMDB");

        local.PosterUrl.Should().Be(Local(local, "season01-poster.jpg"));
        online.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/s2.jpg");
    }

    [Fact]
    public async Task Details_from_an_nfo_respect_fields_the_admin_locked()
    {
        var movie = Movie(preferLocalArtwork: false);
        movie.Title = "Hand-written title";
        movie.LockField(nameof(movie.Title));

        await _mapper.ApplyTextMetadataAsync(movie, new MetadataResult
        {
            Title = "Imported title", SortTitle = "Sort key", Overview = "Imported plot", TmdbId = "277", GenreIds = [28]
        }, false, LocalMetadataProvider.ProviderId, LocalMetadataProvider.LocalProviderName);

        movie.Title.Should().Be("Hand-written title");
        movie.SortTitle.Should().Be("Sort key");
        movie.Overview.Should().Be("Imported plot");
        movie.TmdbId.Should().Be("277");
        movie.LastMetadataRefresh.Should().NotBeNull();
        await _repository.Received(1).SetMediaGenresAsync(movie.Id, Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 28 })));
    }
}
