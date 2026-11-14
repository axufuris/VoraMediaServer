using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryRatingsTargetTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("ratings-target-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedLibrary(VoraDbContext db, string? rating1Provider, string? rating2Provider)
    {
        var id = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = id,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            ThirdPartyRating1ProviderId = rating1Provider,
            ThirdPartyRating2ProviderId = rating2Provider
        });
        return id;
    }

    private static Movie AddMovie(VoraDbContext db, Guid libraryId, string title, decimal? r1, decimal? r2)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = libraryId, ThirdPartyRating1 = r1, ThirdPartyRating2 = r2 };
        db.Set<Movie>().Add(movie);
        return movie;
    }

    [Fact]
    public async Task Targets_items_missing_the_second_rating_when_a_second_provider_is_configured()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, "tmdb_rating", "omdb_rotten_tomatoes");
        var bothSet = AddMovie(db, libraryId, "Both", 7m, 80m);
        var missingSecond = AddMovie(db, libraryId, "MissingRT", 7m, null);
        var missingFirst = AddMovie(db, libraryId, "MissingTmdb", null, 80m);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var ids = (await repo.GetMediaIdsMissingRatingsAsync(libraryId)).ToHashSet();

        Assert.Contains(missingSecond.Id, ids);
        Assert.Contains(missingFirst.Id, ids);
        Assert.DoesNotContain(bothSet.Id, ids);
    }

    [Fact]
    public async Task Ignores_a_missing_second_rating_when_no_second_provider_is_configured()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, "tmdb_rating", null);
        var missingSecond = AddMovie(db, libraryId, "MissingRT", 7m, null);
        var missingFirst = AddMovie(db, libraryId, "MissingTmdb", null, null);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var ids = (await repo.GetMediaIdsMissingRatingsAsync(libraryId)).ToHashSet();

        Assert.DoesNotContain(missingSecond.Id, ids);
        Assert.Contains(missingFirst.Id, ids);
    }

    [Fact]
    public async Task Returns_empty_when_the_library_configures_no_rating_providers()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, null, "");
        AddMovie(db, libraryId, "Unrated", null, null);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        Assert.Empty(await repo.GetMediaIdsMissingRatingsAsync(libraryId));
    }

    private static readonly DateTime Started = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private sealed class EpisodeLibrary
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Dictionary<string, Episode> Episodes { get; } = new();
        public Dictionary<string, TvShow> Shows { get; } = new();
    }

    private static EpisodeLibrary SeedEpisodes(VoraDbContext db, string? rating1Provider = "omdb_imdb", string? rating2Provider = "tmdb_rating")
    {
        var library = new EpisodeLibrary();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = library.Id,
            Name = "Shows",
            Type = LibraryType.TvShow,
            FolderPaths = new List<string> { "/media/tv" },
            ThirdPartyRating1ProviderId = rating1Provider,
            ThirdPartyRating2ProviderId = rating2Provider
        });

        Season SeasonOf(string title)
        {
            var show = new TvShow { Id = Guid.NewGuid(), Title = title, LibraryId = library.Id };
            var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
            db.Set<TvShow>().Add(show);
            db.Set<Season>().Add(season);
            library.Shows[title] = show;
            return season;
        }

        void Add(Season season, string key, int number, string? imdbId = null, string? tvdbId = null, decimal? r1 = null, decimal? r2 = null, DateTime? checkedAt = null, DateTime? missingSince = null)
        {
            var episode = new Episode
            {
                Id = Guid.NewGuid(), Title = key, LibraryId = library.Id, SeasonId = season.Id, EpisodeNumber = number,
                TmdbId = $"{63000 + number}", ImdbId = imdbId, TvdbId = tvdbId,
                ThirdPartyRating1 = r1, ThirdPartyRating2 = r2, RatingsCheckedAt = checkedAt, MissingSince = missingSince
            };
            db.Set<Episode>().Add(episode);
            library.Episodes[key] = episode;
        }

        var saul = SeasonOf("Better Call Saul");
        var andor = SeasonOf("Andor");
        Add(saul, "saul 1", 1, imdbId: "tt3464768");
        Add(andor, "andor 2", 2, imdbId: "tt14290400");
        Add(andor, "andor 1", 1, tvdbId: "8459138");
        Add(andor, "andor 3 tmdb id only", 3);
        Add(andor, "andor 4 rated", 4, imdbId: "tt14290404", r1: 8.1m, r2: 7.9m);
        Add(andor, "andor 5 checked lately", 5, imdbId: "tt14290405", checkedAt: Started.AddDays(-3));
        Add(andor, "andor 6 trashed", 6, imdbId: "tt14290406", missingSince: Started.AddDays(-1));
        Add(andor, "andor 7 checked since", 7, imdbId: "tt14290407", r1: 8.0m, checkedAt: Started.AddMinutes(5));
        db.SaveChanges();
        return library;
    }

    [Fact]
    public async Task The_missing_ratings_pass_takes_episodes_it_can_look_up_show_by_show_in_episode_order()
    {
        using var db = NewContext();
        var library = SeedEpisodes(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var targets = await repo.GetEpisodeIdsToRateAsync(library.Id, missingOnly: true);

        targets.Should().Equal(
            (library.Shows["Andor"].Id, library.Episodes["andor 1"].Id),
            (library.Shows["Andor"].Id, library.Episodes["andor 2"].Id),
            (library.Shows["Better Call Saul"].Id, library.Episodes["saul 1"].Id));
    }

    [Fact]
    public async Task A_forced_ratings_pass_takes_every_episode_with_an_id_of_its_own_not_checked_since_it_was_asked_for()
    {
        using var db = NewContext();
        var library = SeedEpisodes(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var targets = await repo.GetEpisodeIdsToRateAsync(library.Id, missingOnly: false, checkedBefore: Started);

        targets.Select(target => target.EpisodeId).Should().Equal(
            library.Episodes["andor 1"].Id,
            library.Episodes["andor 2"].Id,
            library.Episodes["andor 4 rated"].Id,
            library.Episodes["andor 5 checked lately"].Id,
            library.Episodes["saul 1"].Id);
    }

    [Fact]
    public async Task No_episode_is_rated_in_a_library_without_ratings_providers()
    {
        using var db = NewContext();
        var library = SeedEpisodes(db, rating1Provider: null, rating2Provider: " ");
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        (await repo.GetEpisodeIdsToRateAsync(library.Id, missingOnly: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task Titles_missing_ratings_still_leave_episodes_to_their_own_pass()
    {
        using var db = NewContext();
        var library = SeedEpisodes(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var titles = (await repo.GetMediaIdsMissingRatingsAsync(library.Id)).ToList();

        titles.Should().BeEquivalentTo(library.Shows.Values.Select(show => show.Id));
    }
}
