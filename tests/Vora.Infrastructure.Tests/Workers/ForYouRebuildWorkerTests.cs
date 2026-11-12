using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Recommendations;
using Vora.Application.Settings;
using Vora.Infrastructure.Workers;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests.Workers;

public class ForYouRebuildWorkerTests
{
    private const string AiId = "openai_recommendations";
    private const string LocalId = "local_recommendations";

    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    // The real queue, telling the test each time the worker is done with a rebuild.
    private sealed class ObservedQueue(ForYouRebuildQueue inner) : IForYouRebuildQueue
    {
        public readonly SemaphoreSlim Done = new(0);
        public readonly List<(ForYouRebuild Rebuild, bool CoolDown)> Finishes = new();

        public ChannelReader<ForYouRebuild> Reader => inner.Reader;
        public bool TryQueue(ForYouRebuild rebuild) => inner.TryQueue(rebuild);

        public void Finished(ForYouRebuild rebuild, bool coolDown)
        {
            inner.Finished(rebuild, coolDown);
            lock (Finishes) Finishes.Add((rebuild, coolDown));
            Done.Release();
        }
    }

    private sealed class Scope(IServiceProvider services) : IServiceScope
    {
        public IServiceProvider ServiceProvider => services;
        public void Dispose() { }
    }

    private sealed class Scopes(IServiceProvider services) : IServiceScopeFactory
    {
        public int Created;

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref Created);
            return new Scope(services);
        }
    }

    private readonly Clock _clock = new();
    private readonly IRecommendationSnapshotRepository _snapshots = Substitute.For<IRecommendationSnapshotRepository>();
    private readonly IRecommendationProvider _ai = Substitute.For<IRecommendationProvider>();
    private readonly IRecommendationProvider _local = Substitute.For<IRecommendationProvider>();
    private readonly ObservedQueue _queue;
    private readonly Scopes _scopes;
    private readonly Guid _profile = Guid.NewGuid();

    public ForYouRebuildWorkerTests()
    {
        _ai.Id.Returns(AiId);
        _ai.MakesOutsideCalls.Returns(true);
        _local.Id.Returns(LocalId);
        _local.MakesOutsideCalls.Returns(false);
        _queue = new ObservedQueue(new ForYouRebuildQueue(_clock));

        // Each scope gets the manager the server would build in it.
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IRecommendationManager)).Returns(_ => new RecommendationManager(
            new[] { _ai, _local }, Substitute.For<ISystemSettingsRepository>(), Substitute.For<IRecommendationRepository>(),
            _snapshots, _queue, _clock, NullLogger<RecommendationManager>.Instance));
        _scopes = new Scopes(services);
    }

    private ForYouRebuild Rebuild(string providerId) => new(_profile, null, providerId);

    private static List<RecommendationListDto> Rows(string title) =>
        new() { new RecommendationListDto { Title = title, Weight = 1, LocalItemIds = new() { Guid.NewGuid() } } };

    private async Task<ForYouRebuildWorker> StartAsync()
    {
        var worker = new ForYouRebuildWorker(_queue, _scopes, NullLogger<ForYouRebuildWorker>.Instance);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        return worker;
    }

    private async Task FinishedAsync(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            (await _queue.Done.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_rebuild_runs_in_its_own_scope_and_stores_what_the_provider_found()
    {
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(Rows("AI: Gritty Heists"));
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        await FinishedAsync();
        await worker.StopAsync(TestContext.Current.CancellationToken);

        _scopes.Created.Should().Be(1);
        await _snapshots.Received(1).SaveAsync(_profile, null, AiId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Single().Title == "AI: Gritty Heists"), _clock.Now);
        _queue.Finishes.Should().Equal((Rebuild(AiId), false));
        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue("a rebuild that stored its rows can be asked for again");
    }

    [Fact]
    public async Task A_rebuild_asked_for_again_while_it_runs_runs_once()
    {
        var running = new SemaphoreSlim(0);
        var release = new TaskCompletionSource<IEnumerable<RecommendationListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            running.Release();
            return release.Task;
        });
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        (await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().BeTrue();
        _queue.TryQueue(Rebuild(AiId)).Should().BeFalse();
        _queue.TryQueue(Rebuild(AiId)).Should().BeFalse();
        release.SetResult(Rows("AI: Gritty Heists"));
        await FinishedAsync();
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await _ai.Received(1).GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>());
        await _snapshots.Received(1).SaveAsync(_profile, null, AiId, Arg.Any<IReadOnlyList<RecommendationListDto>>(), Arg.Any<DateTime>());
    }

    // The token limit, a timeout: the page keeps showing what it had.
    [Fact]
    public async Task A_rebuild_that_fails_keeps_the_rows_stored_and_is_not_tried_again_for_ten_minutes()
    {
        var limitReached = true;
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(_ => limitReached
            ? throw new InvalidOperationException("AI monthly token limit reached")
            : Task.FromResult<IEnumerable<RecommendationListDto>>(Rows("AI: Gritty Heists")));
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        await FinishedAsync();

        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
        await _snapshots.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default!);
        _queue.Finishes.Should().Equal((Rebuild(AiId), true));
        _queue.TryQueue(Rebuild(AiId)).Should().BeFalse();

        _clock.Now += ForYouRebuildQueue.Cooldown;
        limitReached = false;
        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        await FinishedAsync();
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await _snapshots.Received(1).SaveAsync(_profile, null, AiId, Arg.Any<IReadOnlyList<RecommendationListDto>>(), Arg.Any<DateTime>());
    }

    // Kept, an empty answer would hide the AI rows for twelve hours; asked
    // again on every page view, it would spend tokens on each one.
    [Fact]
    public async Task An_ai_rebuild_that_finds_nothing_is_not_kept_and_is_not_tried_again_for_ten_minutes()
    {
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>());
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        await FinishedAsync();
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await _snapshots.DidNotReceiveWithAnyArgs().SaveAsync(default, default, default!, default!, default);
        await _snapshots.Received(1).DeleteAsync(_profile, null, AiId);
        _queue.Finishes.Should().Equal((Rebuild(AiId), true));
        _queue.TryQueue(Rebuild(AiId)).Should().BeFalse();
        _clock.Now += ForYouRebuildQueue.Cooldown;
        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_local_rebuild_that_finds_nothing_is_kept_empty()
    {
        _local.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(new List<RecommendationListDto>());
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(LocalId)).Should().BeTrue();
        await FinishedAsync();
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await _snapshots.Received(1).SaveAsync(_profile, null, LocalId, Arg.Is<IReadOnlyList<RecommendationListDto>>(l => l.Count == 0), _clock.Now);
        _queue.Finishes.Should().Equal((Rebuild(LocalId), false));
    }

    [Fact]
    public async Task One_rebuild_failing_does_not_stop_the_next()
    {
        _ai.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<RecommendationListDto>>>(_ => throw new HttpRequestException("timed out"));
        _local.GetRecommendationsAsync(_profile, null, Arg.Any<CancellationToken>()).Returns(Rows("Because you watched Heat"));
        var worker = await StartAsync();

        _queue.TryQueue(Rebuild(AiId)).Should().BeTrue();
        _queue.TryQueue(Rebuild(LocalId)).Should().BeTrue();
        await FinishedAsync(2);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        await _snapshots.Received(1).SaveAsync(_profile, null, LocalId, Arg.Any<IReadOnlyList<RecommendationListDto>>(), Arg.Any<DateTime>());
        _scopes.Created.Should().Be(2);
    }
}
