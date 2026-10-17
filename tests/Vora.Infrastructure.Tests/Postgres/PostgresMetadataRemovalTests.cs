using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Plugins.Dtos;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMetadataRemovalTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Removing_a_movies_metadata_deletes_what_was_looked_up_and_keeps_shared_rows_and_uploads()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var genre = new Genre { Name = "Comedy" };
        var company = new Company { Id = 7_100_001, Name = "Hughes Entertainment" };
        var country = new Country { Iso3166_1 = "ZZ", Name = "Testland" };
        var actor = new Actor { Name = "Macaulay Culkin" };
        var movie = new Movie
        {
            Title = "Home Alone",
            LibraryId = library.Id,
            TmdbId = "771",
            Overview = "An eight-year-old is left behind.",
            PosterUrl = "https://image.tmdb.org/t/p/original/home-alone.jpg",
            LockedFields = [MediaMatchManager.MatchLock],
            Genres = [genre],
            ProductionCompanies = [company],
            OriginCountries = [country],
        };
        var uploaded = new MediaArtwork { MediaItemId = movie.Id, Url = "/api/artwork/custom/upload-1.jpg", Kind = ArtworkKind.Backdrop, ProviderId = "upload", IsUserUploaded = true };
        await using (var seed = database.NewContext())
        {
            seed.AddRange(library, actor, movie);
            seed.Add(new MediaCastMember { ActorId = actor.Id, MediaItemId = movie.Id, CharacterName = "Kevin" });
            seed.Add(new MediaVideo { MediaItemId = movie.Id, VideoKey = "trailer-771", Name = "Trailer", Site = "YouTube", Type = "Trailer" });
            seed.Add(new MediaArtwork { MediaItemId = movie.Id, Url = "https://image.tmdb.org/t/p/original/home-alone.jpg", Kind = ArtworkKind.Poster, ProviderId = "tmdb_artwork" });
            seed.Add(uploaded);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            var localMedia = Substitute.For<ILocalMediaReader>();
            localMedia.ReadNamingAsync(movie.Id).Returns(new LocalNaming("Christmas 2019", 2019, ExternalIdSet.Empty));
            var manager = new MediaMatchManager(
                new MediaRepository(NullLogger<MediaRepository>.Instance, db),
                [],
                Substitute.For<IMediaDedupeManager>(),
                Substitute.For<ITaskQueueManager>(),
                localMedia,
                new MediaArtworkRepository(db),
                NullLogger<MediaMatchManager>.Instance);

            await manager.RemoveMetadataAsync(movie.Id);
        }

        await using var after = database.NewContext();
        var saved = await after.Set<Movie>()
            .Include(m => m.Genres)
            .Include(m => m.ProductionCompanies)
            .Include(m => m.OriginCountries)
            .AsSplitQuery()
            .SingleAsync(m => m.Id == movie.Id, cancellationToken);
        saved.Title.Should().Be("Christmas 2019");
        saved.TmdbId.Should().BeNull();
        saved.Overview.Should().BeNull();
        saved.PosterUrl.Should().BeNull();
        saved.LockedFields.Should().Equal(MediaMatchManager.UnmatchedLock);
        saved.Genres.Should().BeEmpty();
        saved.ProductionCompanies.Should().BeEmpty();
        saved.OriginCountries.Should().BeEmpty();
        (await after.MediaCastMembers.CountAsync(c => c.MediaItemId == movie.Id, cancellationToken)).Should().Be(0);
        (await after.Set<MediaVideo>().CountAsync(v => v.MediaItemId == movie.Id, cancellationToken)).Should().Be(0);
        (await after.Set<MediaArtwork>().Where(a => a.MediaItemId == movie.Id).Select(a => a.Id).ToListAsync(cancellationToken)).Should().Equal(uploaded.Id);
        (await after.Set<Genre>().AnyAsync(g => g.Id == genre.Id, cancellationToken)).Should().BeTrue();
        (await after.Set<Actor>().AnyAsync(a => a.Id == actor.Id, cancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_shows_metadata_resets_every_season_and_episode()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new MediaLibrary { Name = "Shows", Type = LibraryType.TvShow, FolderPaths = ["/tv"] };
        var actor = new Actor { Name = "Guest Star" };
        var show = new TvShow { Title = "The Office (US)", LibraryId = library.Id, TvdbId = "73244", Overview = "A mockumentary." };
        var season = new Season { Title = "The First Season", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id, Overview = "Michael manages." };
        var episode = new Episode { Title = "Pilot", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = library.Id, Overview = "A crew arrives.", TvdbId = "110413" };
        var part = new MediaPart { FilePath = "/tv/The Office/Season 01/The Office - S01E01 - Welcome.mkv", MediaItemId = episode.Id };
        await using (var seed = database.NewContext())
        {
            seed.AddRange(library, actor, show, season, episode, part);
            seed.Add(new MediaCastMember { ActorId = actor.Id, MediaItemId = episode.Id, CharacterName = "Guest" });
            seed.Add(new MediaArtwork { MediaItemId = episode.Id, Url = "https://artworks.thetvdb.com/still.jpg", Kind = ArtworkKind.Backdrop, ProviderId = "tvdb_artwork" });
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            var localMedia = Substitute.For<ILocalMediaReader>();
            localMedia.ReadNamingAsync(show.Id).Returns(new LocalNaming("The Office", null, ExternalIdSet.Empty));
            var manager = new MediaMatchManager(
                new MediaRepository(NullLogger<MediaRepository>.Instance, db),
                [],
                Substitute.For<IMediaDedupeManager>(),
                Substitute.For<ITaskQueueManager>(),
                localMedia,
                new MediaArtworkRepository(db),
                NullLogger<MediaMatchManager>.Instance);

            await manager.RemoveMetadataAsync(show.Id);
        }

        await using var after = database.NewContext();
        var savedShow = await after.Set<TvShow>().AsNoTracking().SingleAsync(s => s.Id == show.Id, cancellationToken);
        var savedSeason = await after.Set<Season>().AsNoTracking().SingleAsync(s => s.Id == season.Id, cancellationToken);
        var savedEpisode = await after.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == episode.Id, cancellationToken);
        savedShow.Title.Should().Be("The Office");
        savedShow.TvdbId.Should().BeNull();
        savedShow.LockedFields.Should().Contain(MediaMatchManager.UnmatchedLock);
        savedSeason.Title.Should().Be("Season 1");
        savedSeason.Overview.Should().BeNull();
        savedEpisode.Title.Should().Be("Welcome");
        savedEpisode.Overview.Should().BeNull();
        savedEpisode.TvdbId.Should().BeNull();
        (await after.MediaCastMembers.CountAsync(c => c.MediaItemId == episode.Id, cancellationToken)).Should().Be(0);
        (await after.Set<MediaArtwork>().CountAsync(a => a.MediaItemId == episode.Id, cancellationToken)).Should().Be(0);
    }
}
