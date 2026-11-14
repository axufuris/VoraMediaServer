using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class DirectorUpNextTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("director-up-next-" + Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task More_from_this_director_follows_the_first_billed_director_and_lists_only_films_they_directed()
    {
        await using var db = NewContext();
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = ["/movies"] };
        var maverick = new Movie { Title = "Top Gun: Maverick", LibraryId = library.Id };
        var oblivion = new Movie { Title = "Oblivion", LibraryId = library.Id };
        var cameo = new Movie { Title = "A Film He Only Appears In", LibraryId = library.Id };
        var otherFilm = new Movie { Title = "Second Director's Film", LibraryId = library.Id };
        var laterDirector = new Actor { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "Billed Later" };
        var kosinski = new Actor { Id = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), Name = "Joseph Kosinski" };
        var cruise = new Actor { Name = "Tom Cruise" };
        db.AddRange(library, maverick, oblivion, cameo, otherFilm, laterDirector, kosinski, cruise);
        db.AddRange(
            new MediaCastMember { ActorId = laterDirector.Id, MediaItemId = maverick.Id, Roles = MediaCastRole.Director, Order = 9 },
            new MediaCastMember { ActorId = cruise.Id, MediaItemId = maverick.Id, Roles = MediaCastRole.Actor | MediaCastRole.Producer, Order = 0 },
            new MediaCastMember { ActorId = kosinski.Id, MediaItemId = maverick.Id, Roles = MediaCastRole.Director, Order = 2 },
            new MediaCastMember { ActorId = kosinski.Id, MediaItemId = oblivion.Id, Roles = MediaCastRole.Director | MediaCastRole.Writer, Order = 4 },
            new MediaCastMember { ActorId = kosinski.Id, MediaItemId = cameo.Id, Roles = MediaCastRole.Actor, Order = 7 },
            new MediaCastMember { ActorId = laterDirector.Id, MediaItemId = otherFilm.Id, Roles = MediaCastRole.Director, Order = 1 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(maverick.Id, null, null, null);

        result.RelatedLists.Should().ContainSingle(list => list.Title == "More from this Director")
            .Which.Items.Select(item => item.Title).Should().Equal("Oblivion");
    }
}
