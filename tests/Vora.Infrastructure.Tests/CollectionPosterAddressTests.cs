using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class CollectionPosterAddressTests
{
    private static VoraDbContext NewContext() => new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("collection-posters-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task Only_collections_whose_poster_has_the_prefix_are_listed()
    {
        await using var db = NewContext();
        var vora = new Collection { Title = "Halloween", PosterUrl = "/api/artwork/vora/holidays/halloween.jpg?v=1" };
        var uploaded = new Collection { Title = "Mine", PosterUrl = "/api/artwork/custom/coll_1.jpg" };
        var none = new Collection { Title = "Bare" };
        db.Collections.AddRange(vora, uploaded, none);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new CollectionRepository(db).GetPosterAddressesStartingWithAsync("/api/artwork/vora/");

        found.Should().ContainSingle().Which.Should().Be(new Vora.Application.Collections.CollectionPosterAddress(vora.Id, vora.PosterUrl));
    }

    [Fact]
    public async Task Poster_addresses_are_replaced_only_for_the_collections_named()
    {
        await using var db = NewContext();
        var moved = new Collection { Title = "Halloween", PosterUrl = "/api/artwork/vora/holidays/halloween.jpg?v=1" };
        var kept = new Collection { Title = "Christmas", PosterUrl = "/api/artwork/vora/holidays/christmas.jpg?v=1" };
        db.Collections.AddRange(moved, kept);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await new CollectionRepository(db).SetPosterUrlsAsync(new Dictionary<Guid, string> { [moved.Id] = "/api/artwork/vora/holidays/halloween.jpg?v=2" });

        db.ChangeTracker.Clear();
        (await db.Collections.SingleAsync(c => c.Id == moved.Id, TestContext.Current.CancellationToken)).PosterUrl.Should().Be("/api/artwork/vora/holidays/halloween.jpg?v=2");
        (await db.Collections.SingleAsync(c => c.Id == kept.Id, TestContext.Current.CancellationToken)).PosterUrl.Should().Be("/api/artwork/vora/holidays/christmas.jpg?v=1");
    }
}
