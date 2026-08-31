using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Actors;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

// The enrichment backlog is drained in batches. An actor that can never resolve
// has to stay out of it: the lookup is by TMDB person id, so a row matched by
// name during a scan (TmdbId 0) fails every run, keeps its null biography, and
// gets handed back again — occupying a slot forever.
public class ActorBacklogQueryTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("actor-backlog-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Actor Add(VoraDbContext db, string name, int tmdbId, string? biography = null, bool isCustom = false)
    {
        var actor = new Actor { Id = Guid.NewGuid(), Name = name, TmdbId = tmdbId, Biography = biography, IsCustom = isCustom };
        db.Actors.Add(actor);
        return actor;
    }

    [Fact]
    public async Task Skips_actors_with_no_tmdb_id()
    {
        using var db = NewContext();
        var resolvable = Add(db, "Tony Bolano", 321432);
        Add(db, "Matched By Name", 0);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await new ActorRepository(db).GetActorIdsMissingMetadataAsync(50);

        ids.Should().ContainSingle().Which.Should().Be(resolvable.Id);
    }

    [Fact]
    public async Task Skips_actors_that_already_have_a_biography()
    {
        using var db = NewContext();
        var pending = Add(db, "Pending", 1);
        // An empty biography still counts as answered — TMDB genuinely returns
        // "" for people it has no write-up for, and asking again won't help.
        Add(db, "Answered With Nothing", 2, biography: string.Empty);
        Add(db, "Answered", 3, biography: "A life.");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ids = await new ActorRepository(db).GetActorIdsMissingMetadataAsync(50);

        ids.Should().ContainSingle().Which.Should().Be(pending.Id);
    }

    [Fact]
    public async Task Skips_custom_actors()
    {
        using var db = NewContext();
        Add(db, "Hand Added", 99, isCustom: true);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new ActorRepository(db).GetActorIdsMissingMetadataAsync(50)).Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_a_stable_batch_so_the_backlog_advances()
    {
        using var db = NewContext();
        for (var i = 1; i <= 10; i++) Add(db, $"Actor {i}", i);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new ActorRepository(db);
        var first = (await repo.GetActorIdsMissingMetadataAsync(4)).ToList();
        var again = (await repo.GetActorIdsMissingMetadataAsync(4)).ToList();

        first.Should().HaveCount(4);
        again.Should().Equal(first);
    }
}
