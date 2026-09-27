using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Media;

public class TrashPurgeOrderTests
{
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();

    private MediaManager Manager() => new(
        _repository,
        Substitute.For<IUserMediaStateRepository>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITaskQueueManager>(),
        Substitute.For<ISystemSettingsRepository>(),
        Array.Empty<ILocalMediaScannerProvider>(),
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IVideoThumbnailStorageService>(),
        Substitute.For<IArtworkThumbnailService>(),
        Substitute.For<ISubtitlePreExtractionManager>(),
        NullLogger<MediaManager>.Instance);

    private void Exists(Guid id) =>
        _repository.GetForBasicUpdateAsync(id).Returns(new Episode { Id = id, Title = "item" });

    // A show deleted before its episodes would cascade them away without the
    // per-episode archive of watch history the purge exists to keep.
    [Fact]
    public async Task Expired_episodes_are_purged_before_the_shells_that_held_them()
    {
        var episode = Guid.NewGuid();
        var season = Guid.NewGuid();
        var show = Guid.NewGuid();
        foreach (var id in new[] { episode, season, show }) Exists(id);
        _repository.GetExpiredMissingMediaIdsAsync(Arg.Any<DateTime>()).Returns([episode]);
        _repository.GetEmptyTrashedSeasonIdsAsync().Returns([season]);
        _repository.GetEmptyTrashedShowIdsAsync().Returns([show]);

        var purged = await Manager().PurgeExpiredTrashAsync(30);

        purged.Should().Be(3);
        Received.InOrder(() =>
        {
            _repository.DeleteMediaItemAsync(episode);
            _repository.GetEmptyTrashedSeasonIdsAsync();
            _repository.DeleteMediaItemAsync(season);
            _repository.GetEmptyTrashedShowIdsAsync();
            _repository.DeleteMediaItemAsync(show);
        });
    }

    [Fact]
    public async Task With_nothing_expired_no_shell_is_purged()
    {
        _repository.GetExpiredMissingMediaIdsAsync(Arg.Any<DateTime>()).Returns(new List<Guid>());
        _repository.GetEmptyTrashedSeasonIdsAsync().Returns(new List<Guid>());
        _repository.GetEmptyTrashedShowIdsAsync().Returns(new List<Guid>());

        var purged = await Manager().PurgeExpiredTrashAsync(30);

        purged.Should().Be(0);
        await _repository.DidNotReceive().DeleteMediaItemAsync(Arg.Any<Guid>());
    }
}
