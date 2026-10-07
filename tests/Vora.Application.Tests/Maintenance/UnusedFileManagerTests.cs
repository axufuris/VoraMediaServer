using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Backups;
using Vora.Application.Maintenance;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Maintenance;

public sealed class UnusedFileManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-unused-" + Guid.NewGuid().ToString("N"));
    private readonly IStorageReferenceRepository _references = Substitute.For<IStorageReferenceRepository>();
    private readonly IArtworkThumbnailService _artworkCache = Substitute.For<IArtworkThumbnailService>();
    private readonly UnusedFileManager _manager;

    private readonly HashSet<Guid> _itemIds = new();
    private readonly HashSet<Guid> _partIds = new();
    private readonly Dictionary<string, HashSet<string>> _named = new(StringComparer.OrdinalIgnoreCase);
    private readonly SubtitleFileReferences _subtitles = new(new HashSet<Guid>(), new List<string>());
    private readonly List<string> _originalPosterUrls = new();
    private readonly List<string> _recordings = new();

    private string Artwork => Path.Combine(_root, "custom_artwork");
    private string Users => Path.Combine(_root, "users");
    private string Thumbnails => Path.Combine(_root, "video-thumbnails");
    private string Subtitles => Path.Combine(_root, "subtitles");
    private string Originals => Path.Combine(_root, "original_artwork_cache");
    private string Plugins => Path.Combine(_root, "plugins");
    private string Backups => Path.Combine(_root, "backups");
    private string Fingerprints => Path.Combine(_root, "fingerprints");
    private string Transcode => Path.Combine(_root, "transcode");
    private string SubtitleCache => Path.Combine(Transcode, "subcache");
    private string Recordings => Path.Combine(_root, "dvr");

    public UnusedFileManagerTests()
    {
        var paths = new StoragePathsOptions
        {
            CustomArtwork = Artwork,
            UserImages = Users,
            VideoThumbnails = Thumbnails,
            Subtitles = Subtitles,
            OriginalArtworkCache = Originals,
            Plugins = Plugins,
            AudioFingerprints = Fingerprints,
            IptvDvr = Recordings
        };

        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = Transcode });
        var thumbnails = Substitute.For<IVideoThumbnailStorageService>();
        thumbnails.RootDirectory.Returns(Thumbnails);
        var subtitleCache = Substitute.For<ISubtitleExtractionService>();
        subtitleCache.GetCacheDirectory(Transcode).Returns(SubtitleCache);
        var backups = Substitute.For<IBackupManager>();
        backups.GetEffectiveDirectoryAsync(Arg.Any<CancellationToken>()).Returns(Backups);

        _references.FindReferencedNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(_named);
        _references.GetMediaItemIdsAsync(Arg.Any<CancellationToken>()).Returns(_itemIds);
        _references.GetMediaPartIdsAsync(Arg.Any<CancellationToken>()).Returns(_partIds);
        _references.GetSubtitleFileReferencesAsync(Arg.Any<CancellationToken>()).Returns(_subtitles);
        _references.GetOriginalPosterUrlsAsync(Arg.Any<CancellationToken>()).Returns(_originalPosterUrls);
        _references.GetRecordingFilePathsAsync(Arg.Any<CancellationToken>()).Returns(_recordings);

        _manager = new UnusedFileManager(_references, settings, thumbnails, subtitleCache, backups, _artworkCache,
            Options.Create(paths), NullLogger<UnusedFileManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Old(string path, int bytes = 10) => Write(path, bytes, DateTime.UtcNow.AddDays(-2));

    private string Fresh(string path) => Write(path, 10, DateTime.UtcNow);

    private static string Write(string path, int bytes, DateTime writtenAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, writtenAt);
        return path;
    }

    private void Reference(string prefix, params string[] names)
    {
        if (!_named.TryGetValue(prefix, out var set)) _named[prefix] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names) set.Add(name);
    }

    private static UnusedFileGroupVM Group(UnusedFilesReportVM report, UnusedFileKind kind) => report.Groups.Single(g => g.Kind == kind);

    [Fact]
    public async Task Artwork_nothing_in_the_database_points_to_is_found_and_removed()
    {
        var used = Old(Path.Combine(Artwork, "media_1_poster_a.jpg"));
        var usedByPath = Old(Path.Combine(Artwork, "music_poster_b.jpg"));
        var orphan = Old(Path.Combine(Artwork, "media_2_poster_c.jpg"), bytes: 300);
        Reference(UnusedFileManager.ArtworkUrlPrefix, "media_1_poster_a.jpg");
        Reference(UnusedFileManager.FolderPrefix(Artwork), "music_poster_b.jpg");

        var scan = await _manager.ScanAsync(TestContext.Current.CancellationToken);

        Group(scan, UnusedFileKind.Artwork).Examples.Should().Equal("media_2_poster_c.jpg");
        Group(scan, UnusedFileKind.Artwork).Bytes.Should().Be(300);
        File.Exists(orphan).Should().BeTrue();

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(orphan).Should().BeFalse();
        File.Exists(used).Should().BeTrue();
        File.Exists(usedByPath).Should().BeTrue();
        _artworkCache.Received(1).RemoveThumbnailsForSource("/api/artwork/custom/media_2_poster_c.jpg");
    }

    [Fact]
    public async Task An_encoded_reference_still_keeps_the_file()
    {
        var file = Old(Path.Combine(Artwork, "coll_1 poster.jpg"));
        Reference(UnusedFileManager.ArtworkUrlPrefix, "coll_1%20poster.jpg");

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public async Task Recently_written_files_dotfiles_subfolders_and_files_Vora_did_not_name_are_left_alone()
    {
        var fresh = Fresh(Path.Combine(Artwork, "media_3_poster_d.jpg"));
        var dotfile = Old(Path.Combine(Artwork, ".keep"));
        var cached = Old(Path.Combine(Artwork, "imagecache", "posters", "abc.jpg"));
        var someoneElses = Old(Path.Combine(Artwork, "poster.jpg"));
        var overlay = Old(Path.Combine(Artwork, $"{Guid.NewGuid()}_overlay_ab12.jpg"));

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(fresh).Should().BeTrue();
        File.Exists(dotfile).Should().BeTrue();
        File.Exists(cached).Should().BeTrue();
        File.Exists(someoneElses).Should().BeTrue();
        File.Exists(overlay).Should().BeFalse();
    }

    [Fact]
    public async Task Profile_pictures_no_profile_uses_are_removed()
    {
        var used = Old(Path.Combine(Users, "profile_a.png"));
        var orphan = Old(Path.Combine(Users, "profile_b.png"));
        Reference(UnusedFileManager.ProfileImageUrlPrefix, "profile_a.png");

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(used).Should().BeTrue();
        File.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task Scrub_thumbnails_of_items_and_parts_that_are_gone_are_removed()
    {
        var keptItem = Guid.NewGuid();
        var keptPart = Guid.NewGuid();
        var goneItem = Guid.NewGuid();
        var gonePart = Guid.NewGuid();
        _itemIds.Add(keptItem);
        _partIds.Add(keptPart);
        string Folder(Guid item) => Path.Combine(Thumbnails, item.ToString("N")[..2], item.ToString("N"));
        var keptSprite = Old(Path.Combine(Folder(keptItem), keptPart.ToString("N"), "sprite.webp"));
        var legacySprite = Old(Path.Combine(Folder(keptItem), "sprite.webp"));
        Old(Path.Combine(Folder(keptItem), gonePart.ToString("N"), "sprite.webp"), bytes: 40);
        Old(Path.Combine(Folder(goneItem), "sprite.webp"), bytes: 50);
        Old(Path.Combine(Folder(goneItem), "thumbnails.vtt"), bytes: 5);
        var stranger = Old(Path.Combine(Thumbnails, "notes", "readme.txt"));

        var scan = await _manager.ScanAsync(TestContext.Current.CancellationToken);
        Group(scan, UnusedFileKind.ScrubThumbnails).Files.Should().Be(3);
        Group(scan, UnusedFileKind.ScrubThumbnails).Bytes.Should().Be(95);

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        Directory.Exists(Folder(goneItem)).Should().BeFalse();
        Directory.Exists(Path.Combine(Folder(keptItem), gonePart.ToString("N"))).Should().BeFalse();
        File.Exists(keptSprite).Should().BeTrue();
        File.Exists(legacySprite).Should().BeTrue();
        File.Exists(stranger).Should().BeTrue();
    }

    [Fact]
    public async Task Downloaded_subtitles_are_kept_by_path_or_track_and_the_rest_removed()
    {
        var item = Guid.NewGuid().ToString("N");
        var byPath = Old(Path.Combine(Subtitles, item[..2], item, $"{Guid.NewGuid():N}.vtt"));
        var byTrack = Guid.NewGuid();
        var byTrackFile = Old(Path.Combine(Subtitles, item[..2], item, $"{byTrack:N}.vtt"));
        var orphanItem = Guid.NewGuid().ToString("N");
        var orphan = Old(Path.Combine(Subtitles, orphanItem[..2], orphanItem, $"{Guid.NewGuid():N}.vtt"));
        var loose = Old(Path.Combine(Subtitles, "readme.txt"));
        _subtitles.FilePaths.Add(byPath);
        _subtitles.TrackIds.Add(byTrack);

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(loose).Should().BeTrue();
        File.Exists(byPath).Should().BeTrue();
        File.Exists(byTrackFile).Should().BeTrue();
        File.Exists(orphan).Should().BeFalse();
        Directory.Exists(Path.Combine(Subtitles, orphanItem[..2], orphanItem)).Should().BeFalse();
    }

    [Fact]
    public async Task Original_artwork_no_overlay_is_built_from_is_removed()
    {
        _originalPosterUrls.Add("https://image.tmdb.org/t/p/w500/abc123.jpg");
        var used = Old(Path.Combine(Originals, "tporiginalabc123.jpg"));
        var orphan = Old(Path.Combine(Originals, "tporiginalold.jpg"));

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(used).Should().BeTrue();
        File.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task Cached_subtitles_for_parts_that_are_gone_are_removed()
    {
        var part = Guid.NewGuid();
        _partIds.Add(part);
        var kept = Old(Path.Combine(SubtitleCache, $"{part}_{Guid.NewGuid()}_abcd.vtt"));
        var gone = Old(Path.Combine(SubtitleCache, $"{Guid.NewGuid()}_{Guid.NewGuid()}_abcd.vtt"));
        var failed = Old(Path.Combine(SubtitleCache, $"{Guid.NewGuid()}_{Guid.NewGuid()}_abcd.failed"));

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(kept).Should().BeTrue();
        File.Exists(gone).Should().BeFalse();
        File.Exists(failed).Should().BeFalse();
    }

    [Fact]
    public async Task Uninstalled_plugins_and_day_old_unfinished_files_are_removed()
    {
        var plugin = Old(Path.Combine(Plugins, "Vora.Plugin.Example", "Vora.Plugin.Example.dll"));
        var uninstalled = Old(Path.Combine(Plugins, "Vora.Plugin.Old", "Vora.Plugin.Old.dll.deleted"));
        var imageTmp = Old(Path.Combine(Artwork, "imagecache", "posters", "x.jpg.tmp"));
        var backupTmp = Old(Path.Combine(Backups, "vora-backup-20261001-010101.zip.tmp"));
        var wav = Old(Path.Combine(Fingerprints, $"fp_{Guid.NewGuid():N}.wav"));
        var subtitleTmp = Old(Path.Combine(SubtitleCache, $"{Guid.NewGuid()}.vtt.tmp"));
        var runningTmp = Fresh(Path.Combine(Backups, "vora-backup-20261006-120000.zip.tmp"));

        await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        File.Exists(plugin).Should().BeTrue();
        File.Exists(uninstalled).Should().BeFalse();
        File.Exists(imageTmp).Should().BeFalse();
        File.Exists(backupTmp).Should().BeFalse();
        File.Exists(wav).Should().BeFalse();
        File.Exists(subtitleTmp).Should().BeFalse();
        File.Exists(runningTmp).Should().BeTrue();
    }

    [Fact]
    public async Task Recordings_Vora_does_not_know_are_reported_and_never_removed()
    {
        var known = Old(Path.Combine(Recordings, "Law_&_Order_(2024)_20261001_200000.mp4"));
        _recordings.Add(Path.ChangeExtension(known, ".ts"));
        var unknown = Old(Path.Combine(Recordings, "Old_Show_20250101_200000.ts"), bytes: 1000);
        var notARecording = Old(Path.Combine(Recordings, "holiday.mp4"));

        var report = await _manager.RemoveAsync(TestContext.Current.CancellationToken);

        var recordings = Group(report, UnusedFileKind.Recordings);
        recordings.Removable.Should().BeFalse();
        recordings.Examples.Should().Equal("Old_Show_20250101_200000.ts");
        report.RemovableBytes.Should().Be(0);
        File.Exists(known).Should().BeTrue();
        File.Exists(unknown).Should().BeTrue();
        File.Exists(notARecording).Should().BeTrue();
    }

    [Fact]
    public void A_folder_that_holds_another_storage_folder_is_never_swept()
    {
        UnusedFileManager.HoldsOtherStorage("/app/data", new[] { "/app/data", "/app/data/custom_artwork" }).Should().BeTrue();
        UnusedFileManager.HoldsOtherStorage("/app/data/subtitles", new[] { "/app/data/subtitles", "/app/data/custom_artwork", null }).Should().BeFalse();
    }

    [Fact]
    public async Task A_scan_removes_nothing()
    {
        var orphan = Old(Path.Combine(Artwork, "media_9_poster_z.jpg"));

        var report = await _manager.ScanAsync(TestContext.Current.CancellationToken);

        report.Removed.Should().BeFalse();
        report.RemovableFiles.Should().Be(1);
        File.Exists(orphan).Should().BeTrue();
    }
}
