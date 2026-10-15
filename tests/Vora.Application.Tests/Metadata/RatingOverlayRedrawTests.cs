using Microsoft.Extensions.Options;
using NSubstitute;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class RatingOverlayRedrawTests
{
    private static readonly DateTime OverlayDrawn = new(2026, 9, 28, 8, 0, 0, DateTimeKind.Utc);

    private readonly MetadataMappingService _service = new(
        Substitute.For<IMediaRepository>(),
        Substitute.For<IMediaArtworkRepository>(),
        Substitute.For<IActorRepository>(),
        Substitute.For<ICollectionRepository>(),
        Substitute.For<IReferenceRepository>(),
        new ReferenceWriteGate(),
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IPluginSettingsProvider>());

    private static Movie Movie(decimal? rating1 = 7.4m, string? name1 = "TMDB", decimal? rating2 = null, string? name2 = null) => new()
    {
        Title = "Heat", ThirdPartyRating1 = rating1, ThirdPartyRating1Name = name1, ThirdPartyRating2 = rating2, ThirdPartyRating2Name = name2,
        LastOverlayGeneratedAt = OverlayDrawn
    };

    [Fact]
    public async Task A_score_that_arrives_later_redraws_the_poster_overlay()
    {
        var movie = Movie();

        await _service.ApplyRatingsAsync(movie, (7.4m, "TMDB", 88m, "Rotten Tomatoes"), forceOverride: false);

        movie.ThirdPartyRating2.Should().Be(88m);
        movie.LastOverlayGeneratedAt.Should().BeNull();
    }

    [Fact]
    public async Task The_same_scores_again_leave_the_overlay_alone()
    {
        var movie = Movie(rating2: 88m, name2: "Rotten Tomatoes");

        await _service.ApplyRatingsAsync(movie, (7.4m, "TMDB", 88m, "Rotten Tomatoes"), forceOverride: false);

        movie.LastOverlayGeneratedAt.Should().Be(OverlayDrawn);
    }

    [Fact]
    public async Task A_score_the_provider_no_longer_has_is_taken_off_the_overlay()
    {
        var movie = Movie(rating2: 88m, name2: "Rotten Tomatoes");

        await _service.ApplyRatingsAsync(movie, (7.4m, "TMDB", null, "Rotten Tomatoes"), forceOverride: false);

        movie.ThirdPartyRating2.Should().BeNull();
        movie.LastOverlayGeneratedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_locked_score_that_is_not_replaced_leaves_the_overlay_alone()
    {
        var movie = Movie();
        movie.LockField(nameof(movie.ThirdPartyRating1));

        await _service.ApplyRatingsAsync(movie, (9.1m, "TMDB", null, null), forceOverride: false);

        movie.ThirdPartyRating1.Should().Be(7.4m);
        movie.LastOverlayGeneratedAt.Should().Be(OverlayDrawn);
    }
}
