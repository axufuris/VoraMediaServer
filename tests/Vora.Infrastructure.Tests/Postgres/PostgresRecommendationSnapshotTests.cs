using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.Providers;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests.Postgres;

// For You used to ask every provider on every request: 65 statements and about
// a second for the local rows on QA's data, and a chat completion after every
// restart for the AI's. It reads the rows each provider last built.
public class PostgresRecommendationSnapshotTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private const string Local = "local_recommendations";
    private const string Ai = "openai_recommendations";

    private sealed class Counter : DbCommandInterceptor
    {
        public readonly List<string> Statements = new();

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            lock (Statements) Statements.Add(command.CommandText);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            lock (Statements) Statements.Add(command.CommandText);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record Seed(Guid ProfileId, Guid OtherProfileId, Guid LibraryId, List<Guid> Movies);

    private async Task<Seed> SeedAsync(int movies = 6)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.NewContext();
        var library = new MediaLibrary { Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var user = new User { Email = $"{tag}@example.com", DisplayName = tag };
        var profile = new UserProfile { Name = "Viewer", UserId = user.Id };
        var other = new UserProfile { Name = "Other", UserId = user.Id };
        var titles = Enumerable.Range(1, movies)
            .Select(i => new Movie { Title = $"Movie {i} {tag}", LibraryId = library.Id, ThirdPartyRating1 = 5 + i * 0.1m })
            .ToList();
        db.AddRange(library, user, profile, other);
        db.AddRange(titles);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seed(profile.Id, other.Id, library.Id, titles.Select(t => t.Id).ToList());
    }

    private static List<RecommendationListDto> Rows(string title, params Guid[] ids) =>
        new() { new RecommendationListDto { Title = title, Description = "Because.", Weight = 1, LocalItemIds = ids.ToList() } };

    private static DateTime At(int hour) => new(2026, 11, 11, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rows_are_stored_and_read_back_and_a_newer_build_replaces_them()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);

        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("First", seed.Movies[0], seed.Movies[1]), At(10));
        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Second", seed.Movies[2]), At(11));

        var stored = await repository.GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: false);
        var only = stored.Providers.Should().ContainSingle().Subject;
        only.ProviderId.Should().Be(Local);
        only.GeneratedAt.Should().Be(At(11));
        only.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Title = "Second",
            Description = "Because.",
            Weight = 1,
            IsAi = false,
            LocalItemIds = new List<Guid> { seed.Movies[2] },
            ExternalTmdbIds = new List<string>()
        });
    }

    // The migration that took "AI: " off the stored AI titles writes the flag
    // as isAi, so that is the name the rows must keep.
    [Fact]
    public async Task An_ai_row_keeps_its_flag_stored_as_isAi()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        var rows = new List<RecommendationListDto> { new() { Title = "Gritty Heists", Weight = 1, IsAi = true, LocalItemIds = [seed.Movies[0]] } };

        await repository.SaveAsync(seed.ProfileId, null, Ai, rows, At(10));

        var stored = await repository.GetAsync(seed.ProfileId, null, new[] { Ai }, withLastActivity: false);
        stored.Providers.Single().Rows.Single().IsAi.Should().BeTrue();
        var flag = await db.Database
            .SqlQueryRaw<string>("SELECT (\"Rows\" -> 0 ->> 'isAi') AS \"Value\" FROM \"RecommendationSnapshots\" WHERE \"ProfileId\" = {0} AND \"ProviderId\" = {1}", seed.ProfileId, Ai)
            .SingleAsync(TestContext.Current.CancellationToken);
        flag.Should().Be("true");
    }

    // A page building the rows and the worker rebuilding them can finish in
    // either order. The one that started later is kept.
    [Fact]
    public async Task An_older_build_finishing_late_does_not_replace_a_newer_one()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);

        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Newer", seed.Movies[0]), At(12));
        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Older", seed.Movies[1]), At(9));

        var stored = await repository.GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: false);
        stored.Providers.Single().Rows.Single().Title.Should().Be("Newer");
        stored.Providers.Single().GeneratedAt.Should().Be(At(12));
    }

    [Fact]
    public async Task Home_and_each_library_and_each_provider_and_each_profile_keep_their_own_rows()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        var otherLibrary = Guid.NewGuid();

        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Home local", seed.Movies[0]), At(10));
        await repository.SaveAsync(seed.ProfileId, null, Ai, Rows("Home AI", seed.Movies[1]), At(10));
        await repository.SaveAsync(seed.ProfileId, seed.LibraryId, Local, Rows("Library local", seed.Movies[2]), At(10));
        await repository.SaveAsync(seed.ProfileId, otherLibrary, Local, Rows("Other library local", seed.Movies[3]), At(10));
        await repository.SaveAsync(seed.OtherProfileId, null, Local, Rows("Other profile", seed.Movies[4]), At(10));

        (await db.RecommendationSnapshots.CountAsync(s => s.ProfileId == seed.ProfileId, TestContext.Current.CancellationToken)).Should().Be(4);
        (await TitlesAsync(repository, seed.ProfileId, null, Local, Ai)).Should().BeEquivalentTo("Home local", "Home AI");
        (await TitlesAsync(repository, seed.ProfileId, null, Ai)).Should().Equal("Home AI");
        (await TitlesAsync(repository, seed.ProfileId, seed.LibraryId, Local, Ai)).Should().Equal("Library local");
        (await TitlesAsync(repository, seed.ProfileId, otherLibrary, Local)).Should().Equal("Other library local");
        (await TitlesAsync(repository, seed.OtherProfileId, null, Local)).Should().Equal("Other profile");

        var home = await db.RecommendationSnapshots.AsNoTracking()
            .SingleAsync(s => s.ProfileId == seed.ProfileId && s.ProviderId == Local && s.LibraryScope == RecommendationSnapshot.AllLibraries, TestContext.Current.CancellationToken);
        home.LibraryScope.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task A_profiles_rows_go_with_the_profile()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Home", seed.Movies[0]), At(10));
        await repository.SaveAsync(seed.ProfileId, seed.LibraryId, Ai, Rows("Library", seed.Movies[1]), At(10));
        await repository.SaveAsync(seed.OtherProfileId, null, Local, Rows("Kept", seed.Movies[2]), At(10));

        await db.UserProfiles.Where(p => p.Id == seed.ProfileId).ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        (await db.RecommendationSnapshots.CountAsync(s => s.ProfileId == seed.ProfileId, TestContext.Current.CancellationToken)).Should().Be(0);
        (await TitlesAsync(repository, seed.OtherProfileId, null, Local)).Should().Equal("Kept");
    }

    // The worker can finish after the profile was deleted.
    [Fact]
    public async Task Nothing_is_stored_for_a_profile_that_no_longer_exists()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        var gone = Guid.NewGuid();

        await repository.SaveAsync(gone, null, Local, Rows("Nobody"), At(10));

        (await db.RecommendationSnapshots.CountAsync(s => s.ProfileId == gone, TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_takes_only_that_providers_rows_for_that_page()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        await repository.SaveAsync(seed.ProfileId, null, Ai, Rows("Home AI", seed.Movies[0]), At(10));
        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Home local", seed.Movies[1]), At(10));
        await repository.SaveAsync(seed.ProfileId, seed.LibraryId, Ai, Rows("Library AI", seed.Movies[2]), At(10));

        await repository.DeleteAsync(seed.ProfileId, null, Ai);

        (await TitlesAsync(repository, seed.ProfileId, null, Local, Ai)).Should().Equal("Home local");
        (await TitlesAsync(repository, seed.ProfileId, seed.LibraryId, Ai)).Should().Equal("Library AI");
    }

    [Fact]
    public async Task The_last_activity_is_the_profiles_latest_play_or_rating_and_comes_with_the_rows()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using (var setup = database.NewContext())
        {
            setup.UserMediaStates.AddRange(
                new UserMediaState { ProfileId = seed.ProfileId, MediaItemId = seed.Movies[0], IsPlayed = true, LastPlayedAt = At(8) },
                new UserMediaState { ProfileId = seed.ProfileId, MediaItemId = seed.Movies[1], ResumePositionSeconds = 600, LastPlayedAt = At(13) },
                new UserMediaState { ProfileId = seed.OtherProfileId, MediaItemId = seed.Movies[2], IsPlayed = true, LastPlayedAt = At(20) });
            setup.UserMediaRatings.AddRange(
                new UserMediaRating { ProfileId = seed.ProfileId, MediaItemId = seed.Movies[3], Rating = 9, RatedAt = At(11) },
                new UserMediaRating { ProfileId = seed.OtherProfileId, MediaItemId = seed.Movies[4], Rating = 9, RatedAt = At(21) });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var db = database.NewContext();
        var repository = new RecommendationSnapshotRepository(db);
        await repository.SaveAsync(seed.ProfileId, null, Local, Rows("Home", seed.Movies[5]), At(12));

        (await repository.GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: true)).LastActivityAt.Should().Be(At(13));
        (await repository.GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: false)).LastActivityAt.Should().BeNull();

        await using (var rate = database.NewContext())
        {
            await rate.UserMediaRatings.Where(r => r.ProfileId == seed.ProfileId).ExecuteUpdateAsync(r => r.SetProperty(x => x.RatedAt, At(15)), TestContext.Current.CancellationToken);
        }
        (await repository.GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: true)).LastActivityAt.Should().Be(At(15));
    }

    [Fact]
    public async Task Rows_that_cannot_be_read_count_as_never_built()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using var db = database.NewContext();
        db.RecommendationSnapshots.Add(new RecommendationSnapshot { ProfileId = seed.ProfileId, LibraryScope = Guid.Empty, ProviderId = Local, Rows = """{"not":"a list"}""", GeneratedAt = At(10) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stored = await new RecommendationSnapshotRepository(db).GetAsync(seed.ProfileId, null, new[] { Local }, withLastActivity: false);

        stored.Providers.Should().BeEmpty();
    }

    // The page is three statements whatever the provider: whether it is on,
    // its stored rows (with the profile's last play and rating for the local
    // rows), and the tiles.
    [Theory]
    [InlineData(Local)]
    [InlineData(Ai)]
    public async Task A_for_you_request_with_stored_rows_is_three_statements_and_calls_no_provider(string providerId)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync();
        await using (var setup = database.NewContext())
        {
            await new RecommendationSnapshotRepository(setup).SaveAsync(
                seed.ProfileId, null, providerId,
                new List<RecommendationListDto>
                {
                    new() { Title = "One", Weight = 1, LocalItemIds = seed.Movies.Take(3).ToList() },
                    new() { Title = "Two", Weight = 2, LocalItemIds = seed.Movies.Skip(3).ToList() },
                },
                DateTime.UtcNow);
        }

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var local = Substitute.For<IRecommendationProvider>();
        local.Id.Returns(Local);
        local.MakesOutsideCalls.Returns(false);
        var ai = Substitute.For<IRecommendationProvider>();
        ai.Id.Returns(Ai);
        ai.MakesOutsideCalls.Returns(true);
        var queue = Substitute.For<IForYouRebuildQueue>();
        var manager = new RecommendationManager(
            new[] { ai, local }, new SystemSettingsRepository(db), new RecommendationRepository(db), new RecommendationSnapshotRepository(db),
            queue, TimeProvider.System, NullLogger<RecommendationManager>.Instance);

        var rows = await manager.GetRecommendationsAsync(seed.ProfileId, null, true, new(), new(), new(), false, providerId);

        rows.Select(r => r.Title).Should().Equal("One", "Two");
        rows[0].Items.Select(i => i.Id).Should().Equal(seed.Movies.Take(3));
        rows[1].Items.Select(i => i.Id).Should().Equal(seed.Movies.Skip(3));
        counter.Statements.Should().HaveCount(3);
        await local.DidNotReceive().GetRecommendationsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await ai.DidNotReceive().GetRecommendationsAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        queue.DidNotReceiveWithAnyArgs().TryQueue(default);
    }

    // The local rows' first build waits for the provider once and stores what
    // it found; the next request reads it.
    [Fact]
    public async Task The_local_rows_are_built_on_the_first_visit_and_read_on_the_next()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var seed = await SeedAsync(movies: 30);
        await using (var setup = database.NewContext())
        {
            setup.UserMediaStates.Add(new UserMediaState { ProfileId = seed.ProfileId, MediaItemId = seed.Movies[0], IsPlayed = true, LastPlayedAt = DateTime.UtcNow.AddHours(-1) });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var first = new Counter();
        await using (var db = database.NewContext(first))
        {
            var rows = await LocalManager(db).GetRecommendationsAsync(seed.ProfileId, seed.LibraryId, true, new(), new(), new(), false, Local);
            rows.Select(r => r.Title).Should().Equal("Top rated you haven't seen");
            rows[0].Items.Should().HaveCount(15).And.NotContain(i => i.Id == seed.Movies[0]);
        }

        var second = new Counter();
        await using (var db = database.NewContext(second))
        {
            var rows = await LocalManager(db).GetRecommendationsAsync(seed.ProfileId, seed.LibraryId, true, new(), new(), new(), false, Local);
            rows.Select(r => r.Title).Should().Equal("Top rated you haven't seen");
        }

        first.Statements.Count.Should().BeGreaterThan(3);
        second.Statements.Should().HaveCount(3);
    }

    private static RecommendationManager LocalManager(VoraDbContext db)
    {
        var recommendations = new RecommendationRepository(db);
        return new RecommendationManager(
            new IRecommendationProvider[] { new LocalRecommendationProvider(recommendations) },
            new SystemSettingsRepository(db), recommendations, new RecommendationSnapshotRepository(db),
            new ForYouRebuildQueue(TimeProvider.System), TimeProvider.System, NullLogger<RecommendationManager>.Instance);
    }

    private static async Task<List<string>> TitlesAsync(RecommendationSnapshotRepository repository, Guid profileId, Guid? libraryId, params string[] providers) =>
        (await repository.GetAsync(profileId, libraryId, providers, withLastActivity: false))
            .Providers.SelectMany(p => p.Rows).Select(r => r.Title).ToList();
}
