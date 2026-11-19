using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Webhooks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Webhooks;

public sealed class MediaWebhookProcessorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vora-webhook-").FullName;
    private readonly string _movies;
    private readonly string _tv;
    private readonly string _music;
    private readonly ITaskQueueManager _queue = Substitute.For<ITaskQueueManager>();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ILibraryRepository _libraries = Substitute.For<ILibraryRepository>();
    private readonly MediaWebhookLog _log = new();
    private readonly MediaWebhookProcessor _processor;
    private readonly Guid _movieLibrary = Guid.NewGuid();
    private readonly Guid _tvLibrary = Guid.NewGuid();
    private readonly Guid _musicLibrary = Guid.NewGuid();

    public MediaWebhookProcessorTests()
    {
        _movies = Directory.CreateDirectory(Path.Combine(_root, "movies")).FullName;
        _tv = Directory.CreateDirectory(Path.Combine(_root, "tv")).FullName;
        _music = Directory.CreateDirectory(Path.Combine(_root, "music")).FullName;

        _settings.GetSettingsAsync().Returns(new ServerSetting());
        _libraries.GetAllProjectedAsync(Arg.Any<Expression<Func<MediaLibrary, MediaWebhookLibrary>>>(), Arg.Any<bool>(), Arg.Any<List<Guid>?>())
            .Returns(new[]
            {
                new MediaWebhookLibrary(_movieLibrary, "Movies", LibraryType.Movie, new List<string> { _movies }, new List<string> { ".TDARR" }),
                new MediaWebhookLibrary(_tvLibrary, "TV", LibraryType.TvShow, new List<string> { _tv }, new List<string>()),
                new MediaWebhookLibrary(_musicLibrary, "Music", LibraryType.Music, new List<string> { _music }, new List<string>()),
            }.AsEnumerable());

        var services = new ServiceCollection()
            .AddSingleton(_settings)
            .AddSingleton(_libraries)
            .AddSingleton(_media)
            .AddSingleton(_queue)
            .BuildServiceProvider();

        _processor = new MediaWebhookProcessor(services.GetRequiredService<IServiceScopeFactory>(), _log, MediaWebhookTimings.Immediate, NullLogger<MediaWebhookProcessor>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(string root, params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private Task Process(LibraryType type, string eventName, params MediaWebhookChange[] changes) =>
        Run(type, eventName, string.Empty, true, new Dictionary<string, string>(), changes);

    private Task Run(LibraryType? type, string eventName, string mappings, bool handleDeletes, Dictionary<string, string> settings, params MediaWebhookChange[] changes) =>
        _processor.ProcessAsync(
            new MediaWebhookWork(Guid.NewGuid(), "Connection", "test_webhook", "Instance", eventName, type, mappings, handleDeletes, settings, changes),
            TestContext.Current.CancellationToken);

    private static MediaWebhookChange Added(string path) => new(MediaWebhookChangeKind.Added, path);

    private static MediaWebhookChange Removed(string path) => new(MediaWebhookChangeKind.Removed, path);

    private static MediaWebhookChange Folder(string path) => new(MediaWebhookChangeKind.FolderRemoved, path);

    private MediaWebhookDelivery OnlyDelivery() => _log.Recent(10).Single();

    [Fact]
    public async Task An_imported_movie_is_ingested_by_itself_with_no_scan_of_the_library()
    {
        var file = Touch(_movies, "Film (2020)", "Film (2020).mkv");

        await Process(LibraryType.Movie, "Download", Added("/movies/Film (2020)/Film (2020).mkv"));

        _queue.Received(1).QueueScanNewFile(_movieLibrary, file);
        _queue.DidNotReceiveWithAnyArgs().QueueScanLibrary(default, default!);
        OnlyDelivery().Ok.Should().BeTrue();
        OnlyDelivery().Path.Should().Be(file);
    }

    [Fact]
    public async Task An_imported_episode_and_a_track_use_their_own_libraries_and_ingests()
    {
        var episode = Touch(_tv, "Show", "Season 1", "e1.mkv");
        var track = Touch(_music, "Artist", "Album", "01.flac");

        await Process(LibraryType.TvShow, "Download", Added("/data/tv/Show/Season 1/e1.mkv"));
        await Process(LibraryType.Music, "Download", Added("/music/Artist/Album/01.flac"));

        _queue.Received(1).QueueScanNewFile(_tvLibrary, episode);
        _queue.Received(1).QueueScanNewMusicFile(_musicLibrary, track);
        _queue.DidNotReceive().QueueScanNewMusicFile(_tvLibrary, Arg.Any<string>());
    }

    [Fact]
    public async Task A_file_that_never_appears_is_reported_with_a_hint_and_not_queued()
    {
        Directory.CreateDirectory(Path.Combine(_movies, "Film (2020)"));

        await Process(LibraryType.Movie, "Download", Added("/movies/Film (2020)/Missing.mkv"));

        _queue.DidNotReceiveWithAnyArgs().QueueScanNewFile(default, default!);
        OnlyDelivery().Ok.Should().BeFalse();
        OnlyDelivery().Outcome.Should().Contain("Not found on disk").And.Contain("path mapping");
    }

    [Fact]
    public async Task A_path_Vora_cannot_place_is_reported_and_nothing_is_queued()
    {
        await Process(LibraryType.Movie, "Download", Added("/elsewhere/Unknown/Film.mkv"));

        _queue.ReceivedCalls().Should().BeEmpty();
        OnlyDelivery().Ok.Should().BeFalse();
        OnlyDelivery().Outcome.Should().Contain("path mapping");
    }

    [Fact]
    public async Task A_mapping_set_for_the_provider_is_used()
    {
        var file = Touch(_movies, "Film (2020)", "Film.mkv");

        await Run(LibraryType.Movie, "Download", $"/data/films => {_movies}", true, new Dictionary<string, string>(), Added("/data/films/Film (2020)/Film.mkv"));

        _queue.Received(1).QueueScanNewFile(_movieLibrary, file);
    }

    [Fact]
    public async Task Files_that_are_not_media_or_are_excluded_are_skipped()
    {
        Touch(_movies, "Film (2020)", "Film.nfo");
        Touch(_movies, "Film (2020)", "Film.TDARR.mkv");

        await Process(LibraryType.Movie, "Download", Added("/movies/Film (2020)/Film.nfo"), Added("/movies/Film (2020)/Film.TDARR.mkv"));

        _queue.DidNotReceiveWithAnyArgs().QueueScanNewFile(default, default!);
        _log.Recent(10).Should().OnlyContain(d => d.Ok);
    }

    [Fact]
    public async Task The_same_file_reported_twice_close_together_is_ingested_once()
    {
        Touch(_tv, "Show", "S1", "e1.mkv");

        await Process(LibraryType.TvShow, "Download", Added("/tv/Show/S1/e1.mkv"));
        await Process(LibraryType.TvShow, "Download", Added("/tv/Show/S1/e1.mkv"));

        _queue.Received(1).QueueScanNewFile(_tvLibrary, Arg.Any<string>());
    }

    [Fact]
    public async Task A_deleted_file_is_checked_and_then_sent_to_the_soft_delete()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_movies, "Film (2020)")).FullName;
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Process(LibraryType.Movie, "MovieFileDelete", Removed("/movies/Film (2020)/Film.mkv"));

        _queue.Received(1).QueueRemoveOrphanedMedia(Path.Combine(folder, "Film.mkv"));
        OnlyDelivery().Ok.Should().BeTrue();
        OnlyDelivery().Outcome.Should().Contain("Media Trash");
    }

    [Fact]
    public async Task A_file_the_webhook_says_is_deleted_but_is_still_on_disk_is_kept()
    {
        Touch(_movies, "Film (2020)", "Film.mkv");

        await Process(LibraryType.Movie, "MovieFileDelete", Removed("/movies/Film (2020)/Film.mkv"));

        _queue.DidNotReceiveWithAnyArgs().QueueRemoveOrphanedMedia(default!);
        OnlyDelivery().Outcome.Should().Contain("still on disk");
    }

    [Fact]
    public async Task Nothing_is_removed_when_the_library_folder_cannot_be_reached()
    {
        Directory.Delete(_movies, recursive: true);
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Process(LibraryType.Movie, "MovieFileDelete", Removed(Path.Combine(_movies, "Film (2020)", "Film.mkv")));

        _queue.DidNotReceiveWithAnyArgs().QueueRemoveOrphanedMedia(default!);
        OnlyDelivery().Ok.Should().BeFalse();
        OnlyDelivery().Outcome.Should().Contain("cannot be reached");
    }

    [Fact]
    public async Task An_upgrade_ingests_the_new_file_and_removes_the_old_one_that_is_gone()
    {
        var added = Touch(_movies, "Film (2020)", "Film.2160p.mkv");
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Process(LibraryType.Movie, "Download",
            Added("/movies/Film (2020)/Film.2160p.mkv"), Removed("/movies/Film (2020)/Film.1080p.mkv"));

        _queue.Received(1).QueueScanNewFile(_movieLibrary, added);
        _queue.Received(1).QueueRemoveOrphanedMedia(Path.Combine(_movies, "Film (2020)", "Film.1080p.mkv"));
    }

    [Fact]
    public async Task A_deleted_series_folder_is_swept_for_files_that_are_gone()
    {
        Directory.CreateDirectory(Path.Combine(_tv, "Show"));
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Process(LibraryType.TvShow, "SeriesDelete", Folder("/tv/Show"));

        _queue.Received(1).QueueRemoveMissingMediaUnder(Path.Combine(_tv, "Show"));
        OnlyDelivery().Ok.Should().BeTrue();
    }

    [Fact]
    public async Task A_folder_removal_never_reaches_a_whole_library_folder()
    {
        await Process(LibraryType.TvShow, "SeriesDelete", Folder(_tv));

        _queue.DidNotReceiveWithAnyArgs().QueueRemoveMissingMediaUnder(default!);
        OnlyDelivery().Ok.Should().BeFalse();
        OnlyDelivery().Outcome.Should().Contain("whole library folder");
    }

    [Fact]
    public async Task A_folder_removal_with_the_library_unreachable_removes_nothing()
    {
        var series = Path.Combine(_tv, "Show");
        Directory.Delete(_tv, recursive: true);
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Process(LibraryType.TvShow, "SeriesDelete", Folder(series));

        _queue.DidNotReceiveWithAnyArgs().QueueRemoveMissingMediaUnder(default!);
        OnlyDelivery().Ok.Should().BeFalse();
    }

    [Fact]
    public async Task A_connection_that_does_not_handle_deletes_still_ingests_but_ignores_removals()
    {
        var added = Touch(_movies, "Film (2020)", "Film.2160p.mkv");
        _media.AnyMediaUnderPathAsync(Arg.Any<string>()).Returns(true);

        await Run(
            LibraryType.Movie, "Download", string.Empty, false, new Dictionary<string, string>(),
            Added("/movies/Film (2020)/Film.2160p.mkv"), Removed("/movies/Film (2020)/Film.1080p.mkv"), Folder("/movies/Film (2020)"));

        _queue.Received(1).QueueScanNewFile(_movieLibrary, added);
        _queue.DidNotReceiveWithAnyArgs().QueueRemoveOrphanedMedia(default!);
        _queue.DidNotReceiveWithAnyArgs().QueueRemoveMissingMediaUnder(default!);
        _log.Recent(10).Count(d => d.Outcome.Contains("does not handle deletes")).Should().Be(2);
    }

    [Fact]
    public async Task A_scan_connection_starts_a_scan_of_its_library_and_needs_no_path()
    {
        _libraries.GetProjectedByIdAsync(_tvLibrary, Arg.Any<Expression<Func<MediaLibrary, string>>>()).Returns("TV");

        await Run(
            null, "added", string.Empty, true,
            new Dictionary<string, string> { [MediaWebhookSettingKeys.ScanLibrary] = _tvLibrary.ToString() },
            new MediaWebhookChange(MediaWebhookChangeKind.ScanLibrary, string.Empty));

        _queue.Received(1).QueueScanLibrary(_tvLibrary, "TV");
        OnlyDelivery().Ok.Should().BeTrue();
        OnlyDelivery().Outcome.Should().Contain("Scan queued for TV");
    }

    [Fact]
    public async Task A_scan_connection_with_no_library_or_a_deleted_one_says_so()
    {
        _libraries.GetProjectedByIdAsync(Arg.Any<Guid>(), Arg.Any<Expression<Func<MediaLibrary, string>>>()).Returns((string?)null);
        var change = new MediaWebhookChange(MediaWebhookChangeKind.ScanLibrary, string.Empty);

        await Run(null, "added", string.Empty, true, new Dictionary<string, string>(), change);
        await Run(null, "added", string.Empty, true, new Dictionary<string, string> { [MediaWebhookSettingKeys.ScanLibrary] = Guid.NewGuid().ToString() }, change);

        _queue.DidNotReceiveWithAnyArgs().QueueScanLibrary(default);
        _log.Recent(10).Should().OnlyContain(d => !d.Ok);
    }

    [Fact]
    public async Task A_connection_with_no_kind_of_library_cannot_place_a_file()
    {
        await Run(null, "added", string.Empty, true, new Dictionary<string, string>(), Added("/movies/Film (2020)/Film.mkv"));

        _queue.ReceivedCalls().Should().BeEmpty();
        OnlyDelivery().Outcome.Should().Contain("kind of library");
    }

}
