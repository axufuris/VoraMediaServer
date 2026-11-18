using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class SmartListPopularOrderTests
{
    private static readonly Guid Library = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();
    private static readonly Guid Cy = Guid.NewGuid();

    private static VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("smartlist-popular-" + Guid.NewGuid().ToString("N")).Options);
        db.Add(new MediaLibrary { Id = Library, Name = "Mixed", Type = LibraryType.Movie, FolderPaths = new List<string> { "/mixed" } });
        return db;
    }

    private static Movie Movie(string title, decimal? rating = null) =>
        new() { Title = title, LibraryId = Library, ThirdPartyRating1 = rating };

    private static StreamSession Play(Guid mediaItemId, Guid? profile, Guid? extraId = null) =>
        new() { MediaItemId = mediaItemId, UserProfileId = profile, ExtraId = extraId, UserId = Guid.NewGuid(), ClientDeviceId = Guid.NewGuid() };

    private static async Task<List<string>> TitlesAsync(VoraDbContext db, Guid? viewer = null, params string[] mediaTypes)
    {
        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            viewer, null, new SmartListRulesDto { MediaTypes = mediaTypes.Length == 0 ? ["Movie", "TvShow"] : mediaTypes.ToList() }, SmartListSortBy.Popular, 10);
        return items.Select(i => i.Title).ToList();
    }

    [Fact]
    public async Task Titles_are_ordered_by_how_many_profiles_played_them()
    {
        await using var db = NewContext();
        var one = Movie("One viewer");
        var three = Movie("Three viewers");
        var two = Movie("Two viewers");
        db.AddRange(one, three, two);
        db.AddRange(Play(one.Id, Ann), Play(two.Id, Ann), Play(two.Id, Ben), Play(three.Id, Ann), Play(three.Id, Ben), Play(three.Id, Cy));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Three viewers", "Two viewers", "One viewer");
    }

    [Fact]
    public async Task One_profile_playing_a_title_again_and_again_counts_once()
    {
        await using var db = NewContext();
        var rewatched = Movie("Rewatched");
        var shared = Movie("Shared");
        db.AddRange(rewatched, shared);
        db.AddRange(Enumerable.Range(0, 6).Select(_ => Play(rewatched.Id, Ann)));
        db.AddRange(Play(shared.Id, Ann), Play(shared.Id, Ben));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Shared", "Rewatched");
    }

    [Fact]
    public async Task A_show_counts_the_profiles_that_played_any_of_its_episodes()
    {
        await using var db = NewContext();
        var show = new TvShow { Title = "The Show", LibraryId = Library };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = Library };
        var first = new Episode { Title = "E1", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = Library };
        var second = new Episode { Title = "E2", EpisodeNumber = 2, SeasonId = season.Id, LibraryId = Library };
        var film = Movie("The Film");
        db.AddRange(show, season, first, second, film);
        db.AddRange(Play(first.Id, Ann), Play(second.Id, Ben), Play(second.Id, Cy), Play(film.Id, Ann));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("The Show", "The Film");
    }

    [Fact]
    public async Task Titles_nobody_has_played_follow_in_order_of_rating_with_unrated_last()
    {
        await using var db = NewContext();
        var played = Movie("Played", 5.0m);
        db.AddRange(played, Movie("Unrated"), Movie("Loved", 9.1m), Movie("Fine", 6.4m));
        db.Add(Play(played.Id, Ann));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Played", "Loved", "Fine", "Unrated");
    }

    [Fact]
    public async Task A_server_nobody_has_played_on_is_top_rated()
    {
        await using var db = NewContext();
        db.AddRange(Movie("Unrated"), Movie("Loved", 9.1m), Movie("Fine", 6.4m));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Loved", "Fine", "Unrated");
    }

    [Fact]
    public async Task Viewers_with_equal_counts_are_ordered_by_rating()
    {
        await using var db = NewContext();
        var lower = Movie("Lower", 6.0m);
        var higher = Movie("Higher", 8.0m);
        db.AddRange(lower, higher);
        db.AddRange(Play(lower.Id, Ann), Play(higher.Id, Ben));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Higher", "Lower");
    }

    [Fact]
    public async Task Trailers_and_plays_with_no_profile_do_not_count()
    {
        await using var db = NewContext();
        var trailers = Movie("Trailers only", 5.0m);
        var anonymous = Movie("No profile", 5.5m);
        var real = Movie("Really played", 4.0m);
        db.AddRange(trailers, anonymous, real);
        db.AddRange(Play(trailers.Id, Ann, extraId: Guid.NewGuid()), Play(trailers.Id, Ben, extraId: Guid.NewGuid()),
            Play(anonymous.Id, null), Play(anonymous.Id, null), Play(real.Id, Cy));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db)).Should().Equal("Really played", "No profile", "Trailers only");
    }

    [Fact]
    public async Task What_everyone_played_is_the_same_for_a_viewer_who_has_played_nothing()
    {
        await using var db = NewContext();
        var popular = Movie("Popular");
        var quiet = Movie("Quiet", 9.0m);
        db.AddRange(popular, quiet);
        db.AddRange(Play(popular.Id, Ann), Play(popular.Id, Ben));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await TitlesAsync(db, viewer: Guid.NewGuid())).Should().Equal("Popular", "Quiet");
        (await TitlesAsync(db, viewer: null)).Should().Equal("Popular", "Quiet");
    }

    [Fact]
    public async Task Ties_keep_the_same_order_every_time()
    {
        await using var db = NewContext();
        db.AddRange(Enumerable.Range(0, 8).Select(i => Movie("Title " + i)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await TitlesAsync(db);
        var second = await TitlesAsync(db);

        second.Should().Equal(first);
    }
}
