using FluentAssertions;
using Vora.Application.Iptv;

namespace Vora.Application.Tests.Iptv;

public class TunerRegistryTests
{
    [Fact]
    public void TryAcquire_allows_up_to_the_limit_then_blocks()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 2, "a", TunerLeaseKind.Live).Should().BeTrue();
        registry.TryAcquire(playlist, 2, "b", TunerLeaseKind.Dvr).Should().BeTrue();
        registry.TryAcquire(playlist, 2, "c", TunerLeaseKind.Timeshift).Should().BeFalse();
        registry.ActiveCount(playlist).Should().Be(2);
    }

    [Fact]
    public void TryAcquire_counts_all_kinds_against_one_budget()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 2, "live", TunerLeaseKind.Live).Should().BeTrue();
        registry.TryAcquire(playlist, 2, "ts", TunerLeaseKind.Timeshift).Should().BeTrue();
        // A DVR start is now blocked because live + timeshift already consume the budget.
        registry.TryAcquire(playlist, 2, "dvr", TunerLeaseKind.Dvr).Should().BeFalse();
    }

    [Fact]
    public void Release_frees_a_slot()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 1, "a", TunerLeaseKind.Live).Should().BeTrue();
        registry.TryAcquire(playlist, 1, "b", TunerLeaseKind.Dvr).Should().BeFalse();

        registry.Release("a");

        registry.TryAcquire(playlist, 1, "b", TunerLeaseKind.Dvr).Should().BeTrue();
    }

    [Fact]
    public void Zero_or_negative_max_means_unlimited()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        for (var i = 0; i < 50; i++)
        {
            registry.TryAcquire(playlist, 0, $"k{i}", TunerLeaseKind.Live).Should().BeTrue();
        }
        registry.ActiveCount(playlist).Should().Be(50);
    }

    [Fact]
    public void Re_acquiring_same_key_does_not_double_count()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 1, "same", TunerLeaseKind.Timeshift).Should().BeTrue();
        registry.TryAcquire(playlist, 1, "same", TunerLeaseKind.Timeshift).Should().BeTrue();
        registry.ActiveCount(playlist).Should().Be(1);
    }

    [Fact]
    public void Budgets_are_independent_per_playlist()
    {
        var registry = new TunerRegistry();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        registry.TryAcquire(p1, 1, "a", TunerLeaseKind.Live).Should().BeTrue();
        registry.TryAcquire(p1, 1, "b", TunerLeaseKind.Live).Should().BeFalse();
        registry.TryAcquire(p2, 1, "c", TunerLeaseKind.Live).Should().BeTrue();
    }

    [Fact]
    public void EvictIdle_removes_only_stale_leases_of_the_given_kind()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 5, "live-old", TunerLeaseKind.Live).Should().BeTrue();
        registry.TryAcquire(playlist, 5, "dvr-old", TunerLeaseKind.Dvr).Should().BeTrue();

        // Everything is older than a zero idle window, but only Live is eligible here.
        var evicted = registry.EvictIdle(TunerLeaseKind.Live, TimeSpan.Zero);

        evicted.Should().ContainSingle().Which.Should().Be("live-old");
        registry.ActiveCount(playlist).Should().Be(1);
    }

    [Fact]
    public void Heartbeat_keeps_a_lease_alive_past_the_idle_window()
    {
        var registry = new TunerRegistry();
        var playlist = Guid.NewGuid();

        registry.TryAcquire(playlist, 5, "live", TunerLeaseKind.Live).Should().BeTrue();
        registry.Heartbeat("live");

        // A generous idle window means the just-heartbeated lease survives.
        var evicted = registry.EvictIdle(TunerLeaseKind.Live, TimeSpan.FromMinutes(5));

        evicted.Should().BeEmpty();
        registry.ActiveCount(playlist).Should().Be(1);
    }
}
