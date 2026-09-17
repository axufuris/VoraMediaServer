using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class CollectionRepositoryExclusionTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("collection-exclusions-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedCollection(VoraDbContext db)
    {
        var id = Guid.NewGuid();
        db.Add(new Collection { Id = id, Title = "MCU" });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task Manual_add_flags_the_item_and_sync_add_does_not()
    {
        using var db = NewContext();
        var repo = new CollectionRepository(db);
        var collectionId = SeedCollection(db);

        var manual = Guid.NewGuid();
        var synced = Guid.NewGuid();
        await repo.AddItemToCollectionAsync(collectionId, manual);
        await repo.AddItemsToCollectionAsync(new List<CollectionItem> { new() { CollectionId = collectionId, MediaItemId = synced } });

        var manuallyAdded = await repo.GetManuallyAddedMediaIdsAsync(collectionId);
        Assert.Contains(manual, manuallyAdded);
        Assert.DoesNotContain(synced, manuallyAdded);
    }

    [Fact]
    public async Task Exclusions_round_trip_and_can_be_cleared()
    {
        using var db = NewContext();
        var repo = new CollectionRepository(db);
        var collectionId = SeedCollection(db);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await repo.AddExcludedMediaIdAsync(collectionId, a);
        await repo.AddExcludedMediaIdAsync(collectionId, b);
        await repo.AddExcludedMediaIdAsync(collectionId, a);

        var excluded = await repo.GetExcludedMediaIdsAsync(collectionId);
        Assert.Equal(new HashSet<Guid> { a, b }, excluded);

        await repo.RemoveExcludedMediaIdAsync(collectionId, a);
        Assert.Equal(new HashSet<Guid> { b }, await repo.GetExcludedMediaIdsAsync(collectionId));
    }

    [Fact]
    public async Task Excluded_ids_are_empty_for_a_fresh_collection()
    {
        using var db = NewContext();
        var repo = new CollectionRepository(db);
        var collectionId = SeedCollection(db);

        Assert.Empty(await repo.GetExcludedMediaIdsAsync(collectionId));
    }
}
