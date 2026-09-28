using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.Ai;
using Vora.Domain.Entities.Ai;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class AiPlaylistRegenerateTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _sam = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("ai-regenerate-" + Guid.NewGuid().ToString("N"))
            .Options);

    private GeneratedMix Mix(GeneratedMixKind kind, Guid? owner = null) => new()
    {
        ProfileId = owner ?? _me,
        Kind = kind,
        Slot = 3,
        Name = "Cruisin' Vibes",
        Prompt = "Cruising in the car",
        TrackOrder = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
    };

    [Fact]
    public async Task Rebuilding_keeps_the_playlist_and_its_place_and_swaps_the_songs()
    {
        await using var db = NewContext();
        var mix = Mix(GeneratedMixKind.Requested);
        db.GeneratedMixes.Add(mix);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var songs = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();

        await new AiPlaylistRepository(db).RebuildRequestAsync(mix.Id, new GeneratedMix { Name = "Longer Drive", Prompt = "Cruising in the car", TrackOrder = songs });

        var saved = await db.GeneratedMixes.SingleAsync(TestContext.Current.CancellationToken);
        saved.Id.Should().Be(mix.Id);
        saved.Slot.Should().Be(3);
        saved.Name.Should().Be("Longer Drive");
        saved.TrackOrder.Should().Equal(songs);
    }

    [Fact]
    public async Task Only_the_owners_request_or_blend_is_deleted()
    {
        await using var db = NewContext();
        var request = Mix(GeneratedMixKind.Requested);
        var weekly = Mix(GeneratedMixKind.AiPlaylist);
        var someoneElses = Mix(GeneratedMixKind.Requested, _sam);
        db.GeneratedMixes.AddRange(request, weekly, someoneElses);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = new AiPlaylistRepository(db);

        (await repo.DeleteAiMixAsync(_me, weekly.Id, AiPlaylistService.DeletableKinds)).Should().BeFalse();
        (await repo.DeleteAiMixAsync(_me, someoneElses.Id, AiPlaylistService.DeletableKinds)).Should().BeFalse();
        (await repo.DeleteAiMixAsync(_me, request.Id, AiPlaylistService.DeletableKinds)).Should().BeTrue();

        (await db.GeneratedMixes.Select(m => m.Id).ToListAsync(TestContext.Current.CancellationToken)).Should().BeEquivalentTo(new[] { weekly.Id, someoneElses.Id });
    }

    [Fact]
    public async Task The_daily_count_is_the_profiles_ai_calls_so_deleting_a_playlist_frees_nothing()
    {
        await using var db = NewContext();
        var now = DateTime.UtcNow;
        db.AiUsageLogs.AddRange(
            new AiUsageLog { PluginId = AiPlaylistService.PluginId, ProfileId = _me, Timestamp = now.AddHours(-1) },
            new AiUsageLog { PluginId = AiPlaylistService.PluginId, ProfileId = _me, Timestamp = now.AddHours(-2) },
            new AiUsageLog { PluginId = AiPlaylistService.PluginId, ProfileId = _me, Timestamp = now.AddDays(-2) },
            new AiUsageLog { PluginId = AiPlaylistService.PluginId, ProfileId = null, Timestamp = now },
            new AiUsageLog { PluginId = AiPlaylistService.PluginId, ProfileId = _sam, Timestamp = now },
            new AiUsageLog { PluginId = "openai_recommendations", ProfileId = _me, Timestamp = now });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new AiPlaylistRepository(db).CountRequestsSinceAsync(_me, now.AddDays(-1))).Should().Be(2);
    }
}
