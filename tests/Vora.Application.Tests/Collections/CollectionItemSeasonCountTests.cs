using Vora.Application.Collections.ViewModels;
using Vora.Application.Libraries.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Collections;

// A show inside a collection has to carry the same season count the rest of the
// app reads off LibraryItemVM, or the shared client caption falls back to a
// different subtitle for the same show depending on where it is rendered.
public class CollectionItemSeasonCountTests
{
    private static Collection CollectionOf(params MediaItem[] items)
    {
        var collection = new Collection { Title = "A collection" };
        foreach (var item in items) collection.Items.Add(item);
        return collection;
    }

    private static CollectionDetailsLibraryItemVM ProjectOne(MediaItem item) =>
        CollectionDetailsVM.Projection.Compile()(CollectionOf(item)).Items.Single();

    private static TvShow ShowWith(int present, int missing) => new()
    {
        Title = "A show",
        NumberOfSeasons = 99,
        Seasons = Enumerable.Range(0, present + missing)
            .Select(i => new Season
            {
                Title = $"Season {i + 1}",
                SeasonNumber = i + 1,
                MissingSince = i < present ? null : new DateTime(2026, 1, 1),
            })
            .ToList(),
    };

    [Fact]
    public void A_show_reports_its_season_count()
    {
        ProjectOne(ShowWith(present: 3, missing: 0)).NumberOfSeasons.Should().Be(3);
    }

    // Seasons whose files have gone missing are soft-deleted, not removed, so a
    // raw Seasons.Count would keep counting them long after they left the disk.
    [Fact]
    public void Seasons_that_are_only_soft_deleted_do_not_count()
    {
        ProjectOne(ShowWith(present: 2, missing: 3)).NumberOfSeasons.Should().Be(2);
    }

    // TvShow.NumberOfSeasons is whatever the metadata provider claimed, which is
    // the total that exists in the world rather than the total this server holds.
    [Fact]
    public void The_count_comes_from_the_library_not_from_provider_metadata()
    {
        var show = ShowWith(present: 2, missing: 0);

        ProjectOne(show).NumberOfSeasons.Should().Be(2).And.NotBe(show.NumberOfSeasons);
    }

    [Fact]
    public void A_movie_reports_no_season_count()
    {
        ProjectOne(new Movie { Title = "A movie" }).NumberOfSeasons.Should().BeNull();
    }

    // The whole point of the field: a show carries the same value whichever list
    // it is rendered in.
    [Fact]
    public void A_show_reports_the_same_count_a_library_listing_would()
    {
        var show = ShowWith(present: 4, missing: 1);

        ProjectOne(show).NumberOfSeasons.Should().Be(LibraryItemVM.Projection.Compile()(show).NumberOfSeasons);
    }

    [Fact]
    public void Both_view_models_declare_the_field_identically()
    {
        var onCollectionItem = typeof(CollectionDetailsLibraryItemVM).GetProperty(nameof(CollectionDetailsLibraryItemVM.NumberOfSeasons));
        var onLibraryItem = typeof(LibraryItemVM).GetProperty(nameof(LibraryItemVM.NumberOfSeasons));

        onCollectionItem.Should().NotBeNull();
        onCollectionItem!.PropertyType.Should().Be(onLibraryItem!.PropertyType);
    }
}
