using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Media;

public class UserMediaStateManagerTests
{
    private readonly IUserMediaStateRepository _repo;
    private readonly IClientNotifier _notifier;
    private readonly ITaskQueueManager _tasks;
    private readonly UserMediaStateManager _manager;

    public UserMediaStateManagerTests()
    {
        _repo = Substitute.For<IUserMediaStateRepository>();
        _notifier = Substitute.For<IClientNotifier>();
        _tasks = Substitute.For<ITaskQueueManager>();
        _manager = new UserMediaStateManager(_repo, _notifier, _tasks);
    }

    [Fact]
    public async Task SetMediaPlayedStateAsync_persists_state_and_notifies()
    {
        var mediaId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        await _manager.SetMediaPlayedStateAsync(mediaId, profileId, isPlayed: true);

        await _repo.Received(1).SetMediaPlayedStateAsync(mediaId, profileId, true);
        await _notifier.Received(1).NotifyMediaItemUpdatedAsync(mediaId);
        await _notifier.Received(1).NotifyUserMediaStateUpdatedAsync(profileId);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    [InlineData(-100)]
    [InlineData(100)]
    public async Task SetMediaRatingAsync_throws_when_rating_out_of_range(double rating)
    {
        var act = async () => await _manager.SetMediaRatingAsync(
            Guid.NewGuid(), Guid.NewGuid(), (decimal)rating, isAdmin: false);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task SetMediaRatingAsync_accepts_rating_in_valid_range(double rating)
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = true });

        var result = await _manager.SetMediaRatingAsync(
            Guid.NewGuid(), Guid.NewGuid(), (decimal)rating, isAdmin: false);

        result.Found.Should().BeTrue();
    }

    [Fact]
    public async Task SetMediaRatingAsync_accepts_null_rating_for_clearing()
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = true });

        var act = async () => await _manager.SetMediaRatingAsync(
            Guid.NewGuid(), Guid.NewGuid(), null, isAdmin: false);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetMediaRatingAsync_returns_unchanged_result_and_does_not_notify_when_not_found()
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = false });

        var mediaId = Guid.NewGuid();
        var result = await _manager.SetMediaRatingAsync(mediaId, Guid.NewGuid(), 7m, isAdmin: false);

        result.Found.Should().BeFalse();
        await _notifier.DidNotReceiveWithAnyArgs().NotifyMediaItemUpdatedAsync(default);
        _tasks.DidNotReceiveWithAnyArgs().QueueGeneratePosterOverlays(default);
    }

    [Fact]
    public async Task SetMediaRatingAsync_notifies_when_found()
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = true });
        var mediaId = Guid.NewGuid();

        await _manager.SetMediaRatingAsync(mediaId, Guid.NewGuid(), 7m, isAdmin: false);

        await _notifier.Received(1).NotifyMediaItemUpdatedAsync(mediaId);
    }

    [Fact]
    public async Task SetMediaRatingAsync_queues_poster_overlay_when_admin_rating_changed()
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = true, ServerAdminRatingChanged = true });
        var mediaId = Guid.NewGuid();

        await _manager.SetMediaRatingAsync(mediaId, Guid.NewGuid(), 7m, isAdmin: true);

        _tasks.Received(1).QueueGeneratePosterOverlays(mediaId);
    }

    [Fact]
    public async Task SetMediaRatingAsync_does_not_queue_overlay_when_only_user_rating_changed()
    {
        _repo.SetMediaRatingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal?>(), Arg.Any<bool>())
            .Returns(new SetMediaRatingResult { Found = true, ServerAdminRatingChanged = false });

        await _manager.SetMediaRatingAsync(Guid.NewGuid(), Guid.NewGuid(), 7m, isAdmin: false);

        _tasks.DidNotReceiveWithAnyArgs().QueueGeneratePosterOverlays(default);
    }
}
