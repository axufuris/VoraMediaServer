using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresEpisodeRatingTargetsTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly DateTime Started = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Episodes_to_rate_come_show_by_show_in_episode_order_and_load_light_for_the_ratings_pass()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new MediaLibrary { Name = "Shows", Type = LibraryType.TvShow, FolderPaths = ["/tv"], ThirdPartyRating1ProviderId = "omdb_imdb" };
        var theWire = new TvShow { Title = "The Wire", SortTitle = "Wire", LibraryId = library.Id };
        var andor = new TvShow { Title = "Andor", LibraryId = library.Id };
        var wireSeason = new Season { Title = "Season 2", SeasonNumber = 2, TvShowId = theWire.Id, LibraryId = library.Id };
        var andorSeason = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = andor.Id, LibraryId = library.Id };
        var bodies = new Episode { Title = "Ebb Tide", EpisodeNumber = 1, SeasonId = wireSeason.Id, LibraryId = library.Id, ImdbId = "tt0749442" };
        var kassa = new Episode { Title = "Kassa", EpisodeNumber = 1, SeasonId = andorSeason.Id, LibraryId = library.Id, TvdbId = "8459138" };
        var reckoning = new Episode { Title = "Reckoning", EpisodeNumber = 3, SeasonId = andorSeason.Id, LibraryId = library.Id, ImdbId = "tt14290402" };
        var tmdbOnly = new Episode { Title = "That Would Be Me", EpisodeNumber = 2, SeasonId = andorSeason.Id, LibraryId = library.Id, TmdbId = "2534997" };
        var rated = new Episode { Title = "Aldhani", EpisodeNumber = 4, SeasonId = andorSeason.Id, LibraryId = library.Id, ImdbId = "tt14290404", ThirdPartyRating1 = 8.2m, RatingsCheckedAt = Started.AddDays(-2) };
        var since = new Episode { Title = "The Axe Forgets", EpisodeNumber = 5, SeasonId = andorSeason.Id, LibraryId = library.Id, ImdbId = "tt14290406", RatingsCheckedAt = Started.AddMinutes(10) };
        await using (var seed = database.NewContext())
        {
            seed.AddRange(library, theWire, andor, wireSeason, andorSeason, bodies, kassa, reckoning, tmdbOnly, rated, since);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using var db = database.NewContext();
        var repository = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var missing = await repository.GetEpisodeIdsToRateAsync(library.Id, missingOnly: true);
        var forced = await repository.GetEpisodeIdsToRateAsync(library.Id, missingOnly: false, checkedBefore: Started);
        var loaded = await repository.GetForRatingsRefreshAsync(kassa.Id) as Episode;

        missing.Should().Equal((andor.Id, kassa.Id), (andor.Id, reckoning.Id), (theWire.Id, bodies.Id));
        forced.Select(target => target.EpisodeId).Should().Equal(kassa.Id, reckoning.Id, rated.Id, bodies.Id);
        loaded.Should().NotBeNull();
        loaded?.Library?.ThirdPartyRating1ProviderId.Should().Be("omdb_imdb");
        loaded?.Season.TvShow.Title.Should().Be("Andor");
    }
}
