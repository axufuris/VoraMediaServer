using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Libraries.Requests;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
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
