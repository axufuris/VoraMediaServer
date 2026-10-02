using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class AiPlaylistSlotTests
{
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _sam = Guid.NewGuid();
    private readonly Guid _alex = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("ai-slots-" + Guid.NewGuid().ToString("N"))
            .Options);

    private GeneratedMix Mix(GeneratedMixKind kind, Guid? partner = null) => new()
    {
        ProfileId = _profile,
        Kind = kind,
        Slot = 1,
        Name = "Mix",
        PartnerProfileId = partner
    };

    [Fact]
    public async Task Each_request_gets_its_own_slot()
    {
        await using var db = NewContext();
        var repo = new AiPlaylistRepository(db);

        await repo.AddRequestAsync(Mix(GeneratedMixKind.Requested), 5);
        await repo.AddRequestAsync(Mix(GeneratedMixKind.Requested), 5);
        await repo.AddRequestAsync(Mix(GeneratedMixKind.Requested), 5);

        var slots = await db.GeneratedMixes.Where(m => m.Kind == GeneratedMixKind.Requested).Select(m => m.Slot).ToListAsync(TestContext.Current.CancellationToken);
        slots.Should().OnlyHaveUniqueItems().And.HaveCount(3);
    }

    [Fact]
    public async Task Blends_with_different_people_get_their_own_slots_and_a_remake_keeps_its_slot()
    {
        await using var db = NewContext();
        var repo = new AiPlaylistRepository(db);

        await repo.ReplaceBlendAsync(Mix(GeneratedMixKind.Blend, _sam));
        await repo.ReplaceBlendAsync(Mix(GeneratedMixKind.Blend, _alex));
        var samSlot = await db.GeneratedMixes.Where(m => m.PartnerProfileId == _sam).Select(m => m.Slot).SingleAsync(TestContext.Current.CancellationToken);

        await repo.ReplaceBlendAsync(Mix(GeneratedMixKind.Blend, _sam));

        var blends = await db.GeneratedMixes.Where(m => m.Kind == GeneratedMixKind.Blend).ToListAsync(TestContext.Current.CancellationToken);
        blends.Should().HaveCount(2);
        blends.Select(m => m.Slot).Should().OnlyHaveUniqueItems();
        blends.Single(m => m.PartnerProfileId == _sam).Slot.Should().Be(samSlot);
    }
}
