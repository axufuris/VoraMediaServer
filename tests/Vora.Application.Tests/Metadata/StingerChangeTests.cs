using Microsoft.Extensions.Options;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Metadata;

public class StingerChangeTests
{
    private static readonly DateTime Analyzed = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OverlayDrawn = new(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc);

    private readonly MetadataMappingService _mapper;

    public StingerChangeTests()
    {
        var pluginSettings = Substitute.For<IPluginSettingsProvider>();
        pluginSettings.GetMetadataLanguageAsync().Returns("eng");
        _mapper = new MetadataMappingService(Substitute.For<IMediaRepository>(), Substitute.For<IMediaArtworkRepository>(), Substitute.For<IActorRepository>(),
            Substitute.For<ICollectionRepository>(), Substitute.For<IReferenceRepository>(), new ReferenceWriteGate(), Options.Create(new StoragePathsOptions()), pluginSettings);
    }

    private static Movie Movie(bool mid = false, bool post = false) => new()
    {
        Title = "Pitch Perfect 2",
        HasMidCreditsStinger = mid,
        HasPostCreditsStinger = post,
        MarkersAnalyzedAt = Analyzed,
        LastOverlayGeneratedAt = OverlayDrawn,
    };

    private Task RefreshAsync(MediaItem item, bool mid, bool post) =>
        _mapper.ApplyTextMetadataAsync(item, new MetadataResult { Title = item.Title, HasMidCreditsStinger = mid, HasPostCreditsStinger = post }, false, "tmdb_metadata", "TMDB");

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, false, false)]
    public async Task A_movie_whose_stingers_change_is_analysed_again_and_its_badge_redrawn(bool wasMid, bool wasPost, bool mid, bool post)
    {
        var movie = Movie(wasMid, wasPost);

        await RefreshAsync(movie, mid, post);

        movie.HasMidCreditsStinger.Should().Be(mid);
        movie.HasPostCreditsStinger.Should().Be(post);
        movie.MarkersAnalyzedAt.Should().BeNull();
        movie.LastOverlayGeneratedAt.Should().BeNull();
    }

    [Fact]
    public async Task The_same_stingers_again_leave_the_markers_and_badge_alone()
    {
        var movie = Movie(mid: true);

        await RefreshAsync(movie, mid: true, post: false);

        movie.MarkersAnalyzedAt.Should().Be(Analyzed);
        movie.LastOverlayGeneratedAt.Should().Be(OverlayDrawn);
    }

    [Fact]
    public async Task An_episode_whose_stingers_change_keeps_its_markers()
    {
        var episode = new Episode { Title = "Pilot", MarkersAnalyzedAt = Analyzed, LastOverlayGeneratedAt = OverlayDrawn };

        await RefreshAsync(episode, mid: false, post: true);

        episode.MarkersAnalyzedAt.Should().Be(Analyzed);
        episode.LastOverlayGeneratedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_refresh_stores_the_keywords_tmdb_gave_and_one_without_any_keeps_those_it_has()
    {
        var movie = Movie();

        await _mapper.ApplyTextMetadataAsync(movie, new MetadataResult { Title = movie.Title, Keywords = new List<string> { "a cappella", "sequel" } }, false, "tmdb_metadata", "TMDB");
        movie.Keywords.Should().Equal("a cappella", "sequel");

        await _mapper.ApplyTextMetadataAsync(movie, new MetadataResult { Title = movie.Title }, false, "tvdb_metadata", "TVDB");
        movie.Keywords.Should().Equal("a cappella", "sequel");
    }
}
