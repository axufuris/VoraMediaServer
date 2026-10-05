using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Settings;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Analysis;

public sealed class LibraryFileCheckTests : IDisposable
{
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IMediaAnalyzerManager _scopedAnalyzer = Substitute.For<IMediaAnalyzerManager>();
    private readonly MediaAnalyzerManager _manager;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-file-check-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _library = Guid.NewGuid();

    public LibraryFileCheckTests()
    {
        Directory.CreateDirectory(_folder);

        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting());

        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediaAnalyzerManager)).Returns(_scopedAnalyzer);
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        _media.GetFileAnalysisTargetIdsAsync(_library).Returns(new List<Guid>());

        _manager = new MediaAnalyzerManager(
            _media,
            Substitute.For<IMediaAnalyzerService>(),
            Substitute.For<IMarkerAssembler>(),
            new AudioIntroDetector(new AudioFingerprintComparer()),
            settings,
            Substitute.For<Vora.Application.Streaming.ISubtitleExtractionService>(),
            new ExternalSubtitleScanner(NullLogger<ExternalSubtitleScanner>.Instance),
            Substitute.For<ITaskQueueManager>(),
            Substitute.For<IClientNotifier>(),
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(),
            scopes,
            Microsoft.Extensions.Options.Options.Create(new StoragePathsOptions()),
            NullLogger<MediaAnalyzerManager>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string Write(string name, int bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private string WriteSubtitle(string name, string content = "1\r\n00:00:01,000 --> 00:00:02,000\r\nHello\r\n")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private PartFileStateDto Part(string path, long? size, bool analyzed = true, params string[] sidecars) => new()
    {
        PartId = Guid.NewGuid(),
        FilePath = path,
        FileSizeBytes = size,
        LastAnalyzedAt = analyzed ? DateTime.UtcNow.AddDays(-7) : null,
        ExternalSubtitlePaths = sidecars.ToList()
    };

    private void Library(params PartFileStateDto[] parts) =>
        _media.GetLibraryPartFileStatesAsync(_library).Returns(parts.ToList());

    private Task Analyze() => _manager.TriggerLibraryFileAnalysisAsync(_library, "Shows", TestContext.Current.CancellationToken);

    [Fact]
    public async Task Only_items_that_need_it_are_analyzed()
    {
        var target = Guid.NewGuid();
        Library();
        _media.GetFileAnalysisTargetIdsAsync(_library).Returns(new List<Guid> { target });
        _media.GetDisplayTitlesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new Dictionary<Guid, string> { [target] = "Pilot" });

        await Analyze();

        await _scopedAnalyzer.Received(1).AnalyzeMediaFileAsync(target, Arg.Any<CancellationToken>());
        await _scopedAnalyzer.ReceivedWithAnyArgs(1).AnalyzeMediaFileAsync(default, TestContext.Current.CancellationToken);
        await _media.DidNotReceive().GetAllMediaItemIdsByLibraryAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_file_replaced_under_the_same_name_is_sent_back_for_analysis()
    {
        var replaced = Part(Write("Pilot.mkv", 20), size: 10);
        var unchanged = Part(Write("Episode 2.mkv", 10), size: 10);
        Library(replaced, unchanged);

        await Analyze();

        await _media.Received(1).MarkPartsChangedOnDiskAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { replaced.PartId })));
    }

    [Fact]
    public async Task Nothing_is_marked_when_nothing_changed_or_the_file_is_missing()
    {
        Library(
            Part(Write("Pilot.mkv", 10), size: 10),
            Part(Path.Combine(_folder, "Gone.mkv"), size: 10),
            Part(Write("Never analyzed.mkv", 30), size: 10, analyzed: false));

        await Analyze();

        await _media.DidNotReceiveWithAnyArgs().MarkPartsChangedOnDiskAsync(default!);
    }

    [Fact]
    public async Task An_unreachable_folder_changes_nothing()
    {
        Library(Part(Path.Combine(_folder, "offline", "Pilot.mkv"), size: 10, sidecars: Path.Combine(_folder, "offline", "Pilot.en.srt")));

        await Analyze();

        await _media.DidNotReceiveWithAnyArgs().MarkPartsChangedOnDiskAsync(default!);
        await _media.DidNotReceiveWithAnyArgs().SyncExternalSubtitleTracksAsync(default, default!);
    }

    [Fact]
    public async Task A_new_sidecar_is_picked_up_without_analyzing_the_video()
    {
        var video = Write("Pilot.mkv", 10);
        var sidecar = WriteSubtitle("Pilot.en.srt");
        var part = Part(video, size: 10);
        Library(part);

        await Analyze();

        await _media.Received(1).SyncExternalSubtitleTracksAsync(part.PartId, Arg.Is<List<MediaSubtitleTrack>>(tracks =>
            tracks.Count == 1 && tracks[0].ExternalFilePath == sidecar && tracks[0].Language == "en"));
        await _scopedAnalyzer.DidNotReceiveWithAnyArgs().AnalyzeMediaFileAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_removed_sidecar_is_dropped()
    {
        var video = Write("Pilot.mkv", 10);
        var part = Part(video, size: 10, sidecars: Path.Combine(_folder, "Pilot.en.srt"));
        Library(part);

        await Analyze();

        await _media.Received(1).SyncExternalSubtitleTracksAsync(part.PartId, Arg.Is<List<MediaSubtitleTrack>>(tracks => tracks.Count == 0));
    }

    [Fact]
    public async Task Sidecars_already_known_are_left_alone()
    {
        var video = Write("Pilot.mkv", 10);
        var sidecar = WriteSubtitle("Pilot.en.srt");
        WriteSubtitle("Episode 2.en.srt");
        Library(Part(video, size: 10, sidecars: sidecar));

        await Analyze();

        await _media.DidNotReceiveWithAnyArgs().SyncExternalSubtitleTracksAsync(default, default!);
    }

    [Fact]
    public async Task An_empty_sidecar_is_not_offered_as_a_subtitle()
    {
        var video = Write("Pilot.mkv", 10);
        WriteSubtitle("Pilot.en.srt", string.Empty);
        var part = Part(video, size: 10);
        Library(part);

        await Analyze();

        await _media.DidNotReceiveWithAnyArgs().SyncExternalSubtitleTracksAsync(default, default!);
    }

    [Fact]
    public async Task A_known_sidecar_that_is_only_blank_lines_is_dropped()
    {
        var video = Write("Pilot.mkv", 10);
        var sidecar = WriteSubtitle("Pilot.en.srt", "\uFEFF\r\n\r\n  \r\n");
        var part = Part(video, size: 10, sidecars: sidecar);
        Library(part);

        await Analyze();

        await _media.Received(1).SyncExternalSubtitleTracksAsync(part.PartId, Arg.Is<List<MediaSubtitleTrack>>(tracks => tracks.Count == 0));
    }
}
