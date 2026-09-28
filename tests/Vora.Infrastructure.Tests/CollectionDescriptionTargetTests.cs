using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class CollectionDescriptionTargetTests
{
    [Fact]
    public async Task Only_never_asked_unlocked_tmdb_collections_in_the_library_are_awaiting()
    {
        await using var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("collection-descriptions-" + Guid.NewGuid().ToString("N")).Options);
        var library = Guid.NewGuid();
        var awaiting = new Collection { Title = "Bourne", TmdbId = 31562, LibraryId = library };
        var askedNone = new Collection { Title = "Alien", TmdbId = 8091, LibraryId = library, Description = string.Empty };
        var described = new Collection { Title = "Toy Story", TmdbId = 10194, LibraryId = library, Description = "Toys." };
        var locked = new Collection { Title = "Mine", TmdbId = 1, LibraryId = library };
        locked.LockField(nameof(Collection.Description));
        var manual = new Collection { Title = "Favourites", LibraryId = library };
        var otherLibrary = new Collection { Title = "Elsewhere", TmdbId = 2, LibraryId = Guid.NewGuid() };
        db.Collections.AddRange(awaiting, askedNone, described, locked, manual, otherLibrary);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var targets = await new CollectionRepository(db).GetCollectionsAwaitingDescriptionAsync(library);

        targets.Select(t => t.CollectionId).Should().Equal(awaiting.Id);
        targets.Single().TmdbId.Should().Be(31562);
    }
}
