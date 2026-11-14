using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Actors;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Settings;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresCastRefreshTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_refresh_replaces_each_persons_roles_and_drops_people_no_longer_credited()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var movie = new Movie { Title = "Top Gun: Maverick", LibraryId = library.Id, TmdbId = "361743", LastMetadataRefresh = DateTime.UtcNow.AddDays(-30) };
        var cruise = new Actor { Name = "Tom Cruise", TmdbId = 500 };
        var stuntPilot = new Actor { Name = "Kevin LaRosa II", TmdbId = 1925013 };
        var assistant = new Actor { Name = "Scott Robertson", TmdbId = 1580131 };
        var secondUnit = new Actor { Name = "Andrew Stahl", TmdbId = 1338372 };
        var supervisor = new Actor { Name = "Robert E. Kay", TmdbId = 1404244 };
        var director = new Actor { Name = "Joseph Kosinski", TmdbId = 107113 };
        await using (var seed = database.NewContext())
        {
            seed.AddRange(library, movie, cruise, stuntPilot, assistant, secondUnit, supervisor, director);
            seed.AddRange(
                new MediaCastMember { ActorId = cruise.Id, MediaItemId = movie.Id, CharacterName = "Maverick", Roles = MediaCastRole.Actor | MediaCastRole.Producer, Order = 0 },
                new MediaCastMember { ActorId = stuntPilot.Id, MediaItemId = movie.Id, CharacterName = "Pilot", Roles = MediaCastRole.Actor | MediaCastRole.Director, Order = 1 },
                new MediaCastMember { ActorId = assistant.Id, MediaItemId = movie.Id, Roles = MediaCastRole.Director, Order = 55 },
                new MediaCastMember { ActorId = secondUnit.Id, MediaItemId = movie.Id, Roles = MediaCastRole.Director, Order = 56 },
                new MediaCastMember { ActorId = supervisor.Id, MediaItemId = movie.Id, Roles = MediaCastRole.Director, Order = 57 },
                new MediaCastMember { ActorId = director.Id, MediaItemId = movie.Id, Roles = MediaCastRole.Director, Order = 117 });
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var db = database.NewContext())
        {
            var repository = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
            var mapper = new MetadataMappingService(repository, Substitute.For<IMediaArtworkRepository>(), new ActorRepository(db),
                Substitute.For<ICollectionRepository>(), Substitute.For<IReferenceRepository>(), new ReferenceWriteGate(),
                Options.Create(new StoragePathsOptions()), Substitute.For<IPluginSettingsProvider>());
            var item = await repository.GetForMetadataSyncAsync(movie.Id) ?? throw new InvalidOperationException("The seeded movie is missing.");

            await mapper.ApplyTextMetadataAsync(item, new MetadataResult
            {
                Title = movie.Title,
                TmdbId = movie.TmdbId,
                Cast =
                [
                    new CastMemberResult { TmdbId = 500, Name = "Tom Cruise", CharacterName = "Maverick", Roles = CastRole.Actor | CastRole.Producer },
                    new CastMemberResult { TmdbId = 1925013, Name = "Kevin LaRosa II", CharacterName = "Pilot", Roles = CastRole.Actor },
                    new CastMemberResult { TmdbId = 107113, Name = "Joseph Kosinski", Roles = CastRole.Director },
                    new CastMemberResult { TmdbId = 9033, Name = "Ehren Kruger", Roles = CastRole.Writer }
                ]
            }, forceOverride: true, "tmdb_metadata", "TMDB");
            await repository.UpdateMediaItemAsync(item);
        }

        await using var after = database.NewContext();
        var cast = await after.MediaCastMembers.AsNoTracking()
            .Where(c => c.MediaItemId == movie.Id)
            .OrderBy(c => c.Order)
            .Select(c => new { c.Actor.Name, c.Roles, c.Order })
            .ToListAsync(cancellationToken);
        cast.Should().Equal(
            new { Name = "Tom Cruise", Roles = MediaCastRole.Actor | MediaCastRole.Producer, Order = 0 },
            new { Name = "Kevin LaRosa II", Roles = MediaCastRole.Actor, Order = 1 },
            new { Name = "Joseph Kosinski", Roles = MediaCastRole.Director, Order = 2 },
            new { Name = "Ehren Kruger", Roles = MediaCastRole.Writer, Order = 3 });

        var details = await new MediaRepository(NullLogger<MediaRepository>.Instance, after).GetMediaDetailsAsync(movie.Id, null);
        (details?.Details.Directors).Should().Equal("Joseph Kosinski");
        (await after.Set<Actor>().CountAsync(a => a.Id == assistant.Id, cancellationToken)).Should().Be(1);
    }
}
