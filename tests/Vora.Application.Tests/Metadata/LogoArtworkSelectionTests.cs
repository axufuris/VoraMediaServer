using Microsoft.Extensions.Options;
using NSubstitute;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class LogoArtworkSelectionTests
{
    private readonly IMediaArtworkRepository _artworkRepository = Substitute.For<IMediaArtworkRepository>();
    private readonly IPluginSettingsProvider _pluginSettings = Substitute.For<IPluginSettingsProvider>();
    private readonly MetadataMappingService _service;

    public LogoArtworkSelectionTests()
    {
        _pluginSettings.GetMetadataLanguageAsync().Returns("eng");
        _service = new MetadataMappingService(
            Substitute.For<IMediaRepository>(),
            _artworkRepository,
            Substitute.For<IActorRepository>(),
            Substitute.For<ICollectionRepository>(),
            Substitute.For<IReferenceRepository>(),
            new ReferenceWriteGate(),
            Options.Create(new StoragePathsOptions()),
            _pluginSettings);
    }

    private static MediaArtwork Logo(string url, string? language, double? vote = 5) => new()
    {
        Url = url,
        Kind = ArtworkKind.Logo,
        Language = language,
        VoteAverage = vote,
        ProviderId = "tmdb_artwork"
    };

    [Fact]
    public async Task A_movie_takes_the_logo_in_the_servers_metadata_language()
    {
        var movie = new Movie { Title = "Dune" };
        var artwork = new List<MediaArtwork>
        {
            Logo("https://example.test/ja.png", "ja", vote: 9),
            Logo("https://example.test/en.png", "en", vote: 4),
        };

        var updated = await _service.ApplyArtworkAsync(movie, artwork, forceOverride: false);

        updated.Should().BeTrue();
        movie.LogoUrl.Should().Be("https://example.test/en.png");
    }

    [Fact]
    public async Task A_non_english_server_prefers_its_own_language_over_english()
    {
        _pluginSettings.GetMetadataLanguageAsync().Returns("jpn");
        var movie = new Movie { Title = "Dune" };
        var artwork = new List<MediaArtwork>
        {
            Logo("https://example.test/en.png", "en", vote: 9),
            Logo("https://example.test/ja.png", "ja", vote: 1),
        };

        await _service.ApplyArtworkAsync(movie, artwork, forceOverride: false);

        movie.LogoUrl.Should().Be("https://example.test/ja.png");
    }

    [Fact]
    public async Task With_no_match_on_language_the_best_rated_logo_wins()
    {
        var movie = new Movie { Title = "Dune" };
        var artwork = new List<MediaArtwork>
        {
            Logo("https://example.test/de.png", "de", vote: 2),
            Logo("https://example.test/fr.png", "fr", vote: 8),
        };

        await _service.ApplyArtworkAsync(movie, artwork, forceOverride: false);

        movie.LogoUrl.Should().Be("https://example.test/fr.png");
    }

    [Fact]
    public async Task An_item_with_no_logo_art_keeps_a_null_logo()
    {
        var movie = new Movie { Title = "Dune" };
        var artwork = new List<MediaArtwork>
        {
            new() { Url = "https://example.test/poster.jpg", Kind = ArtworkKind.Poster, ProviderId = "tmdb_artwork" }
        };

        await _service.ApplyArtworkAsync(movie, artwork, forceOverride: false);

        movie.LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_locked_logo_survives_a_refresh()
    {
        var movie = new Movie { Title = "Dune", LogoUrl = "https://example.test/chosen-by-hand.png" };
        movie.LockField(nameof(movie.LogoUrl));

        var updated = await _service.ApplyArtworkAsync(movie, new List<MediaArtwork> { Logo("https://example.test/en.png", "en") }, forceOverride: false);

        movie.LogoUrl.Should().Be("https://example.test/chosen-by-hand.png");
        updated.Should().BeFalse();
    }

    [Fact]
    public async Task A_forced_refresh_overrides_even_a_locked_logo()
    {
        var movie = new Movie { Title = "Dune", LogoUrl = "https://example.test/chosen-by-hand.png" };
        movie.LockField(nameof(movie.LogoUrl));

        await _service.ApplyArtworkAsync(movie, new List<MediaArtwork> { Logo("https://example.test/en.png", "en") }, forceOverride: true);

        movie.LogoUrl.Should().Be("https://example.test/en.png");
    }

    [Fact]
    public async Task An_episode_gets_no_logo_of_its_own()
    {
        var episode = new Episode { Title = "The Pilot", SeasonId = Guid.NewGuid() };

        await _service.ApplyArtworkAsync(episode, new List<MediaArtwork> { Logo("https://example.test/en.png", "en") }, forceOverride: false);

        episode.LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_season_gets_no_logo_of_its_own()
    {
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = Guid.NewGuid() };

        await _service.ApplyArtworkAsync(season, new List<MediaArtwork> { Logo("https://example.test/en.png", "en") }, forceOverride: false);

        season.LogoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Posters_and_backdrops_still_apply_alongside_a_logo()
    {
        var movie = new Movie { Title = "Dune" };
        var artwork = new List<MediaArtwork>
        {
            new() { Url = "https://example.test/poster.jpg", Kind = ArtworkKind.Poster, VoteAverage = 9, ProviderId = "tmdb_artwork" },
            new() { Url = "https://example.test/backdrop.jpg", Kind = ArtworkKind.Backdrop, VoteAverage = 9, ProviderId = "tmdb_artwork" },
            Logo("https://example.test/en.png", "en"),
        };

        await _service.ApplyArtworkAsync(movie, artwork, forceOverride: false);

        movie.PosterUrl.Should().Be("https://example.test/poster.jpg");
        movie.BackgroundUrl.Should().Be("https://example.test/backdrop.jpg");
        movie.LogoUrl.Should().Be("https://example.test/en.png");
    }
}
