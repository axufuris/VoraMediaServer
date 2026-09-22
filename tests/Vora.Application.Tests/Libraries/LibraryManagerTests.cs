using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Libraries.Requests;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
using Vora.Application.Thumbnails;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Libraries;

public class LibraryManagerTests
{
    private readonly ILibraryRepository _repo;
    private readonly IServiceProvider _services;
    private readonly IFolderWatcherService _watcher;
    private readonly ITaskQueueManager _queue;
    private readonly IClientNotifier _notifier;
    private readonly LibraryManager _manager;

    public LibraryManagerTests()
    {
        _repo = Substitute.For<ILibraryRepository>();
        _services = Substitute.For<IServiceProvider>();
        _watcher = Substitute.For<IFolderWatcherService>();
        _queue = Substitute.For<ITaskQueueManager>();
        _notifier = Substitute.For<IClientNotifier>();

        _manager = new LibraryManager(_repo, _services, _watcher, _queue, _notifier);
    }

    private IVideoThumbnailManager WireThumbnailManager()
    {
        var thumbnails = Substitute.For<IVideoThumbnailManager>();
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var factory = Substitute.For<IServiceScopeFactory>();

        scopeProvider.GetService(typeof(IVideoThumbnailManager)).Returns(thumbnails);
        scope.ServiceProvider.Returns(scopeProvider);
        factory.CreateScope().Returns(scope);
        _services.GetService(typeof(IServiceScopeFactory)).Returns(factory);

        return thumbnails;
    }

    // Deleting a library removes its rows moments later via a bulk delete, so
    // reading each item back to null its thumbnail columns is work thrown away —
    // and it did a read plus a SaveChanges per item on one DbContext, which is
    // quadratic once change detection re-scans everything tracked so far. Only
    // the files need to go.
    [Fact]
    public async Task DeleteLibraryAsync_purges_thumbnail_files_without_the_per_item_column_reset()
    {
        var thumbnails = WireThumbnailManager();
        var libraryId = Guid.NewGuid();

        await _manager.DeleteLibraryAsync(libraryId, TestContext.Current.CancellationToken);

        // Asserting on the same token also proves the delete threads it through
        // rather than dropping it and starting an uncancellable purge.
        await thumbnails.Received(1).PurgeLibraryThumbnailFilesAsync(libraryId, TestContext.Current.CancellationToken);
        await thumbnails.DidNotReceive().PurgeLibraryThumbnailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteLibraryAsync_stops_the_folder_watcher()
    {
        WireThumbnailManager();
        var libraryId = Guid.NewGuid();

        await _manager.DeleteLibraryAsync(libraryId, TestContext.Current.CancellationToken);

        _watcher.Received(1).StopWatching(libraryId);
    }

    [Fact]
    public async Task CreateLibraryAsync_queues_initial_scan_when_folder_paths_present()
    {
        var libraryId = Guid.NewGuid();
        _repo.CreateLibraryAsync(Arg.Any<Domain.Entities.Library.MediaLibrary>()).Returns(libraryId);

        var request = new CreateLibraryRequest
        {
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        };

        var result = await _manager.CreateLibraryAsync(request);

        result.Should().Be(libraryId);
        _queue.Received(1).QueueLibraryAdded(libraryId, "Movies", Arg.Any<bool>());
    }

    [Fact]
    public async Task CreateLibraryAsync_does_not_queue_scan_when_folder_paths_empty()
    {
        _repo.CreateLibraryAsync(Arg.Any<Domain.Entities.Library.MediaLibrary>()).Returns(Guid.NewGuid());

        var request = new CreateLibraryRequest
        {
            Name = "Empty",
            Type = LibraryType.Movie,
            FolderPaths = new List<string>()
        };

        await _manager.CreateLibraryAsync(request);

        _queue.DidNotReceive().QueueLibraryAdded(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CreateLibraryAsync_starts_folder_watcher_when_real_time_enabled()
    {
        var libraryId = Guid.NewGuid();
        _repo.CreateLibraryAsync(Arg.Any<Domain.Entities.Library.MediaLibrary>()).Returns(libraryId);

        var request = new CreateLibraryRequest
        {
            Name = "Watched",
            Type = LibraryType.TvShow,
            FolderPaths = new List<string> { "/media/shows" },
            EnableRealTimeWatching = true
        };

        await _manager.CreateLibraryAsync(request);

        _watcher.Received(1).StartWatching(libraryId, Arg.Is<IEnumerable<string>>(p => p.Contains("/media/shows")));
    }

    [Fact]
    public async Task CreateLibraryAsync_does_not_start_watcher_when_real_time_disabled()
    {
        _repo.CreateLibraryAsync(Arg.Any<Domain.Entities.Library.MediaLibrary>()).Returns(Guid.NewGuid());

        var request = new CreateLibraryRequest
        {
            Name = "Unwatched",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            EnableRealTimeWatching = false
        };

        await _manager.CreateLibraryAsync(request);

        _watcher.DidNotReceive().StartWatching(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task CreateLibraryAsync_applies_default_movie_regex_when_none_supplied()
    {
        Domain.Entities.Library.MediaLibrary? captured = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => captured = l))
            .Returns(Guid.NewGuid());

        var request = new CreateLibraryRequest
        {
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            ScannerRegex = null
        };

        await _manager.CreateLibraryAsync(request);

        captured.Should().NotBeNull();
        captured!.ScannerRegex.Should().NotBeNullOrWhiteSpace();
        captured.ScannerRegex.Should().Contain("Year");
    }

    [Fact]
    public async Task CreateLibraryAsync_applies_default_tv_regex_when_none_supplied()
    {
        Domain.Entities.Library.MediaLibrary? captured = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => captured = l))
            .Returns(Guid.NewGuid());

        var request = new CreateLibraryRequest
        {
            Name = "Shows",
            Type = LibraryType.TvShow,
            FolderPaths = new List<string> { "/media/shows" }
        };

        await _manager.CreateLibraryAsync(request);

        captured!.ScannerRegex.Should().Contain("Season");
        captured.ScannerRegex.Should().Contain("Episode");
    }

    [Fact]
    public async Task CreateLibraryAsync_keeps_custom_regex_when_supplied()
    {
        Domain.Entities.Library.MediaLibrary? captured = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => captured = l))
            .Returns(Guid.NewGuid());

        var customRegex = "^(?<Title>.+)$";
        var request = new CreateLibraryRequest
        {
            Name = "Custom",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            ScannerRegex = customRegex
        };

        await _manager.CreateLibraryAsync(request);

        captured!.ScannerRegex.Should().Be(customRegex);
    }

    [Fact]
    public async Task ToggleWatchingAsync_starts_watcher_when_enabling()
    {
        var libraryId = Guid.NewGuid();
        var library = new Domain.Entities.Library.MediaLibrary
        {
            Id = libraryId,
            Name = "L",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        };
        _repo.GetForUpdateAsync(libraryId).Returns(library);

        await _manager.ToggleWatchingAsync(libraryId, enable: true);

        _watcher.Received(1).StartWatching(libraryId, library.FolderPaths);
        _watcher.DidNotReceive().StopWatching(libraryId);
    }

    [Fact]
    public async Task ToggleWatchingAsync_stops_watcher_when_disabling()
    {
        var libraryId = Guid.NewGuid();
        var library = new Domain.Entities.Library.MediaLibrary
        {
            Id = libraryId,
            Name = "L",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        };
        _repo.GetForUpdateAsync(libraryId).Returns(library);

        await _manager.ToggleWatchingAsync(libraryId, enable: false);

        _watcher.Received(1).StopWatching(libraryId);
        _watcher.DidNotReceive().StartWatching(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>());
    }
}
