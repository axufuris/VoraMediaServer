using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Libraries.Requests;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
using Vora.Application.Thumbnails;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Libraries;

public class LibraryManagerTests
{
    private readonly ILibraryRepository _repo;
    private readonly IServiceProvider _services;
    private readonly IFolderWatcherService _watcher;
    private readonly ITaskQueueManager _queue;
    private readonly IClientNotifier _notifier;
    private readonly LibraryManager _manager;
    private readonly List<Guid> _libraryItems = new() { Guid.NewGuid(), Guid.NewGuid() };

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
        var media = Substitute.For<Vora.Application.Media.IMediaRepository>();
        media.GetAllMediaItemIdsByLibraryAsync(Arg.Any<Guid>()).Returns(_libraryItems);
        scopeProvider.GetService(typeof(Vora.Application.Media.IMediaRepository)).Returns(media);
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
    public async Task DeleteLibraryAsync_stops_the_watcher_then_waits_for_the_librarys_tasks_and_items_before_deleting()
    {
        var thumbnails = WireThumbnailManager();
        var libraryId = Guid.NewGuid();

        await _manager.DeleteLibraryAsync(libraryId, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _watcher.StopWatching(libraryId);
            _queue.WaitForLibraryTasksToStopAsync(libraryId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && _libraryItems.All(ids.Contains)), TestContext.Current.CancellationToken);
            thumbnails.PurgeLibraryThumbnailFilesAsync(libraryId, TestContext.Current.CancellationToken);
            _repo.DeleteLibraryAsync(libraryId, TestContext.Current.CancellationToken);
        });
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
    public async Task CreateLibraryAsync_scans_a_tv_library_with_the_default_pattern_when_none_is_supplied()
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

        captured.Should().NotBeNull();
        var scanned = Vora.Plugins.Providers.Local.LocalScannerPatterns.EffectiveTvEpisode(captured?.ScannerRegex);
        scanned.Should().Contain("Season").And.Contain("Episode");
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

    [Fact]
    public async Task Turning_thumbnails_off_hands_the_removal_to_the_queue_instead_of_purging_inline()
    {
        var thumbnails = WireThumbnailManager();
        var library = new Vora.Domain.Entities.Library.MediaLibrary
        {
            Id = Guid.NewGuid(),
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            EnableVideoPreviewThumbnails = true
        };
        _repo.GetForUpdateAsync(library.Id).Returns(library);

        await _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest { Name = "Movies", FolderPaths = new List<string> { "/media/movies" }, EnableVideoPreviewThumbnails = false });

        _queue.Received(1).QueueRemoveLibraryVideoThumbnails(library.Id, "Movies");
        await thumbnails.DidNotReceive().PurgeLibraryThumbnailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_home_video_library_never_looks_for_intros_or_credits()
    {
        Domain.Entities.Library.MediaLibrary? saved = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => saved = l)).Returns(Guid.NewGuid());

        await _manager.CreateLibraryAsync(new CreateLibraryRequest
        {
            Name = "Family",
            Type = LibraryType.HomeVideo,
            EnableIntroDetection = true,
            EnableCreditsDetection = true,
            EnablePreviewDetection = true,
            EnableVideoPreviewThumbnails = true,
        });

        saved.Should().NotBeNull();
        saved?.EnableIntroDetection.Should().BeFalse();
        saved?.EnableCreditsDetection.Should().BeFalse();
        saved?.EnablePreviewDetection.Should().BeFalse();
        saved?.EnableVideoPreviewThumbnails.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_tv_library_reads_episode_names_automatically()
    {
        Domain.Entities.Library.MediaLibrary? saved = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => saved = l)).Returns(Guid.NewGuid());

        await _manager.CreateLibraryAsync(new CreateLibraryRequest { Name = "Shows", Type = LibraryType.TvShow });

        saved.Should().NotBeNull();
        saved?.ScannerRegex.Should().BeNull();
    }

    [Fact]
    public async Task Saving_a_tv_library_that_kept_an_old_default_pattern_moves_it_to_automatic()
    {
        const string oldDefault = @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";
        var library = new Domain.Entities.Library.MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, ScannerRegex = oldDefault, FolderPaths = new List<string> { "/media/tv" } };
        _repo.GetForUpdateAsync(library.Id).Returns(library);

        await _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest { Name = "Shows", FolderPaths = new List<string> { "/media/tv" }, ScannerRegex = oldDefault });

        library.ScannerRegex.Should().BeNull();
    }

    [Theory]
    [InlineData(@"(?<Season>\d+)x(?<Episode>\d+", "isn't a valid regular expression")]
    [InlineData(@"(?<Season>\d+)x(?<Number>\d+)", "needs an Episode or AirDate group")]
    public async Task A_tv_pattern_the_scanner_could_not_use_is_refused_with_the_reason(string pattern, string reason)
    {
        const string kept = @"(?<Season>\d+)x(?<Episode>\d+) - (?<EpisodeTitle>.*)";
        var library = new Domain.Entities.Library.MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, ScannerRegex = kept, FolderPaths = new List<string> { "/media/tv" } };
        _repo.GetForUpdateAsync(library.Id).Returns(library);

        var save = () => _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest { Name = "Shows", FolderPaths = new List<string> { "/media/tv" }, ScannerRegex = pattern });

        (await save.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(reason);
        library.ScannerRegex.Should().Be(kept);
    }

    [Fact]
    public async Task A_tv_library_on_a_built_in_pattern_shows_as_automatic()
    {
        var id = Guid.NewGuid();
        _repo.GetProjectedByIdAsync(id, Arg.Any<System.Linq.Expressions.Expression<Func<Domain.Entities.Library.MediaLibrary, MediaLibraryVM>>>())
            .Returns(new MediaLibraryVM { Id = id, Type = "TvShow", ScannerRegex = Vora.Plugins.Providers.Local.LocalScannerPatterns.TvEpisode });

        (await _manager.GetLibraryByIdAsync(id))?.ScannerRegex.Should().BeNull();
    }

    [Fact]
    public async Task The_file_name_check_asks_the_scanner_with_no_pattern_for_a_built_in_one()
    {
        var scanner = Substitute.For<ILocalMediaScannerProvider>();
        scanner.Id.Returns("vora_local_scanner");
        scanner.PreviewEpisodeFileNamesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>())
            .Returns(new Vora.Plugins.Dtos.EpisodeNamePreviewDto { FilesFound = 1, ReadFromName = 1, Files = [new Vora.Plugins.Dtos.EpisodeNamePreviewFileDto("Show/Season 1/Show.1x01 - Pilot.mkv", Vora.Plugins.Dtos.EpisodeNamePreviewOutcome.Name, 1, 1, null, "Pilot")] });
        var scope = Substitute.For<IServiceScope>();
        var scopeProvider = Substitute.For<IServiceProvider>();
        var factory = Substitute.For<IServiceScopeFactory>();
        var settings = Substitute.For<Vora.Application.Settings.ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new Domain.Entities.Settings.ServerSetting());
        scopeProvider.GetService(typeof(Vora.Application.Settings.ISystemSettingsRepository)).Returns(settings);
        scopeProvider.GetService(typeof(IEnumerable<ILocalMediaScannerProvider>)).Returns(new[] { scanner });
        scope.ServiceProvider.Returns(scopeProvider);
        factory.CreateScope().Returns(scope);
        _services.GetService(typeof(IServiceScopeFactory)).Returns(factory);

        var preview = await _manager.PreviewEpisodeFileNamesAsync(new PreviewEpisodeFileNamesRequest
        {
            FolderPaths = ["/media/tv"],
            ScannerRegex = Vora.Plugins.Providers.Local.LocalScannerPatterns.TvEpisode
        });

        await scanner.Received(1).PreviewEpisodeFileNamesAsync(Arg.Is<IReadOnlyList<string>>(f => f.Single() == "/media/tv"), null, Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>());
        preview.Files.Single().Outcome.Should().Be(EpisodeNameOutcome.Name);
        preview.Files.Single().Episode.Should().Be(1);
    }

    [Fact]
    public async Task The_file_name_check_needs_a_folder()
    {
        var check = () => _manager.PreviewEpisodeFileNamesAsync(new PreviewEpisodeFileNamesRequest { FolderPaths = [] });

        await check.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_tv_librarys_own_pattern_is_kept()
    {
        const string custom = @"(?<Season>\d+)x(?<Episode>\d+) - (?<EpisodeTitle>.*)";
        var library = new Domain.Entities.Library.MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/media/tv" } };
        _repo.GetForUpdateAsync(library.Id).Returns(library);

        await _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest { Name = "Shows", FolderPaths = new List<string> { "/media/tv" }, ScannerRegex = custom });

        library.ScannerRegex.Should().Be(custom);
    }

    private Domain.Entities.Library.MediaLibrary Held(LibraryType type, LocalMetadataMode mode = LocalMetadataMode.Off, bool preferLocalArtwork = false)
    {
        var library = new Domain.Entities.Library.MediaLibrary
        {
            Id = Guid.NewGuid(),
            Name = "Library",
            Type = type,
            FolderPaths = new List<string> { "/media/library" },
            LocalMetadataMode = mode,
            PreferLocalArtwork = preferLocalArtwork
        };
        _repo.GetForUpdateAsync(library.Id).Returns(library);
        return library;
    }

    private Task SaveAsync(Domain.Entities.Library.MediaLibrary library, int localMetadataMode, bool preferLocalArtwork) =>
        _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest
        {
            Name = library.Name,
            FolderPaths = library.FolderPaths.ToList(),
            ArtworkProviderId = library.ArtworkProviderId,
            LocalMetadataMode = localMetadataMode,
            PreferLocalArtwork = preferLocalArtwork
        });

    [Fact]
    public async Task A_new_library_reads_no_nfo_files_and_prefers_no_local_images_unless_asked()
    {
        Domain.Entities.Library.MediaLibrary? created = null;
        _repo.CreateLibraryAsync(Arg.Do<Domain.Entities.Library.MediaLibrary>(l => created = l)).Returns(Guid.NewGuid());

        await _manager.CreateLibraryAsync(new CreateLibraryRequest { Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/media/movies" } });

        var library = Assert.IsType<Domain.Entities.Library.MediaLibrary>(created);
        library.LocalMetadataMode.Should().Be(LocalMetadataMode.Off);
        library.PreferLocalArtwork.Should().BeFalse();
    }

    [Theory]
    [InlineData(LibraryType.Movie, 2, LocalMetadataMode.Prefer, true)]
    [InlineData(LibraryType.TvShow, 1, LocalMetadataMode.MatchOnly, true)]
    [InlineData(LibraryType.HomeVideo, 2, LocalMetadataMode.Off, false)]
    [InlineData(LibraryType.Music, 1, LocalMetadataMode.Off, false)]
    [InlineData(LibraryType.Movie, 7, LocalMetadataMode.Off, true)]
    public async Task Reading_local_files_is_kept_only_for_film_and_tv_libraries(LibraryType type, int requested, LocalMetadataMode savedMode, bool savedPreference)
    {
        var library = Held(type);

        await SaveAsync(library, requested, preferLocalArtwork: true);

        library.LocalMetadataMode.Should().Be(savedMode);
        library.PreferLocalArtwork.Should().Be(savedPreference);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task Changing_how_a_library_reads_local_files_queues_one_pass_over_what_it_already_holds(int mode, bool preferLocalArtwork)
    {
        var library = Held(LibraryType.Movie);

        await SaveAsync(library, mode, preferLocalArtwork);

        _queue.Received(1).QueueLocalMetadataSync(library.Id, "Library");
    }

    [Fact]
    public async Task A_client_that_does_not_know_these_settings_leaves_them_as_they_are()
    {
        var library = Held(LibraryType.Movie, LocalMetadataMode.Prefer, preferLocalArtwork: true);

        await _manager.UpdateLibraryAsync(library.Id, new UpdateLibraryRequest { Name = library.Name, FolderPaths = library.FolderPaths.ToList() });

        library.LocalMetadataMode.Should().Be(LocalMetadataMode.Prefer);
        library.PreferLocalArtwork.Should().BeTrue();
        _queue.DidNotReceive().QueueLocalMetadataSync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task Saving_a_library_without_touching_those_settings_queues_no_pass()
    {
        var library = Held(LibraryType.Movie, LocalMetadataMode.Prefer, preferLocalArtwork: true);

        await SaveAsync(library, (int)LocalMetadataMode.Prefer, preferLocalArtwork: true);

        _queue.DidNotReceive().QueueLocalMetadataSync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task Libraries_are_listed_alphabetically_whatever_order_they_were_added_in()
    {
        _repo.GetAllProjectedAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Vora.Domain.Entities.Library.MediaLibrary, Vora.Application.Libraries.ViewModels.LibrarySummaryVM>>>(), true, Arg.Any<List<Guid>>())
            .Returns(new[] { "Shows", "music videos", "Movies", "Audiobooks" }
                .Select(name => new Vora.Application.Libraries.ViewModels.LibrarySummaryVM { Id = Guid.NewGuid(), Name = name })
                .ToList());

        var libraries = await _manager.GetLibrariesAsync(true, new List<Guid>());

        libraries.Select(l => l.Name).Should().Equal("Audiobooks", "Movies", "music videos", "Shows");
    }
}
