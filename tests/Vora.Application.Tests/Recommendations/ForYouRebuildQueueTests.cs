using Vora.Application.Recommendations;

namespace Vora.Application.Tests.Recommendations;

public class ForYouRebuildQueueTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private readonly Clock _clock = new();
    private readonly ForYouRebuild _home = new(Guid.NewGuid(), null, "openai_recommendations");

    private List<ForYouRebuild> Queued(ForYouRebuildQueue queue)
    {
        var queued = new List<ForYouRebuild>();
        while (queue.Reader.TryRead(out var rebuild)) queued.Add(rebuild);
        return queued;
    }

    [Fact]
    public void A_rebuild_already_queued_or_running_is_not_queued_twice()
    {
        var queue = new ForYouRebuildQueue(_clock);

        queue.TryQueue(_home).Should().BeTrue();
        queue.TryQueue(_home).Should().BeFalse();
        Queued(queue).Should().Equal(_home);

        // Taken off the queue but still running.
        queue.TryQueue(_home).Should().BeFalse();
        Queued(queue).Should().BeEmpty();
    }

    [Fact]
    public void Each_profile_page_and_provider_is_its_own_rebuild()
    {
        var queue = new ForYouRebuildQueue(_clock);
        var library = _home with { LibraryId = Guid.NewGuid() };
        var local = _home with { ProviderId = "local_recommendations" };
        var other = _home with { ProfileId = Guid.NewGuid() };

        new[] { _home, library, local, other }.Select(queue.TryQueue).ToList().Should().AllBeEquivalentTo(true);
        Queued(queue).Should().Equal(_home, library, local, other);
    }

    [Fact]
    public void A_rebuild_that_finished_can_be_queued_again_at_once()
    {
        var queue = new ForYouRebuildQueue(_clock);
        queue.TryQueue(_home);
        Queued(queue);

        queue.Finished(_home, coolDown: false);

        queue.TryQueue(_home).Should().BeTrue();
    }

    // A provider that fails or finds nothing would otherwise be run again on
    // every page view.
    [Fact]
    public void A_rebuild_that_failed_or_kept_nothing_waits_ten_minutes()
    {
        var queue = new ForYouRebuildQueue(_clock);
        queue.TryQueue(_home);
        Queued(queue);

        queue.Finished(_home, coolDown: true);

        queue.TryQueue(_home).Should().BeFalse();
        _clock.Now += TimeSpan.FromMinutes(9.9);
        queue.TryQueue(_home).Should().BeFalse();
        Queued(queue).Should().BeEmpty();

        _clock.Now += TimeSpan.FromMinutes(0.1);
        queue.TryQueue(_home).Should().BeTrue();
        Queued(queue).Should().Equal(_home);
    }

    [Fact]
    public void The_cooldown_is_per_rebuild()
    {
        var queue = new ForYouRebuildQueue(_clock);
        var local = _home with { ProviderId = "local_recommendations" };
        queue.TryQueue(_home);
        Queued(queue);
        queue.Finished(_home, coolDown: true);

        queue.TryQueue(local).Should().BeTrue();
    }
}
