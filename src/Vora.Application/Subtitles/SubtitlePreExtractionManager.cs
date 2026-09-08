using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Domain.Enums;

namespace Vora.Application.Subtitles;

public class SubtitlePreExtractionManager : ISubtitlePreExtractionManager
{
    // Extraction reads the whole container, so it must never compete with a
    // stream for the same disk. While anything is transcoding the pass parks
    // here and re-checks, rather than queueing behind it or racing it.
    private static readonly TimeSpan TranscodeBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxTranscodeWait = TimeSpan.FromMinutes(30);

    private readonly IMediaRepository _mediaRepository;
    private readonly ILibraryRepository _libraryRepository;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ISubtitleExtractionService _extractor;
    private readonly ITranscodeService _transcodeService;
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly ILogger<SubtitlePreExtractionManager> _logger;

    public SubtitlePreExtractionManager(
        IMediaRepository mediaRepository,
        ILibraryRepository libraryRepository,
        ISystemSettingsRepository settingsRepo,
        ISubtitleExtractionService extractor,
        ITranscodeService transcodeService,
        Vora.Plugins.Interfaces.ITaskProgressReporter progress,
        ILogger<SubtitlePreExtractionManager> logger)
    {
        _mediaRepository = mediaRepository;
        _libraryRepository = libraryRepository;
        _settingsRepo = settingsRepo;
        _extractor = extractor;
        _transcodeService = transcodeService;
        _progress = progress;
        _logger = logger;
    }

    public static bool IsExtractableSubtitleCodec(string? codec) =>
        !BestPathDecisionManager.IsImageSubtitleCodec(codec);

    public async Task<string> ResolveCacheDirectoryRootAsync() =>
        StreamManager.ResolveTempDirectory(await _settingsRepo.GetSettingsAsync());

    public async Task PreExtractForItemAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync()) return;

        var targets = await _mediaRepository.GetSubtitleExtractionTargetsForItemAsync(mediaItemId);
        await RunAsync(targets, cancellationToken);
    }

    public async Task PreExtractForLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync()) return;

        var targets = await _mediaRepository.GetSubtitleExtractionTargetsForLibraryAsync(libraryId);
        await RunAsync(targets, cancellationToken);
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync()) return;

        var libraries = await _libraryRepository.GetAllProjectedAsync(l => new { l.Id, l.Type });
        var videoLibraries = libraries
            .Where(l => Thumbnails.VideoThumbnailManager.IsVideoBearingLibrary(l.Type))
            .Select(l => l.Id)
            .ToList();

        await SweepOrphanedCacheAsync();

        foreach (var libraryId in videoLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = await _mediaRepository.GetSubtitleExtractionTargetsForLibraryAsync(libraryId);
            await RunAsync(targets, cancellationToken);
        }
    }

    // Deletions that never pass through MediaManager.DeleteMediaAsync — a dedupe
    // merge dropping a part, for one — leave cache files nothing owns. The
    // backfill is the only pass that sees every live part id, so it is the only
    // place that can tell an orphan from another library's entry.
    private async Task SweepOrphanedCacheAsync()
    {
        var root = await ResolveCacheDirectoryRootAsync();
        var livePartIds = await _mediaRepository.GetAllMediaPartIdsAsync();

        foreach (var orphanedPartId in _extractor.ListCachedPartIds(root).Where(id => !livePartIds.Contains(id)))
        {
            _logger.LogInformation("Removing cached subtitles for media part {PartId}, which no longer exists.", orphanedPartId);
            _extractor.PurgePart(root, orphanedPartId);
        }
    }

    public async Task PurgeItemAsync(Guid mediaItemId)
    {
        var root = await ResolveCacheDirectoryRootAsync();
        var targets = await _mediaRepository.GetSubtitleExtractionTargetsForItemAsync(mediaItemId);
        foreach (var target in targets)
        {
            _extractor.PurgePart(root, target.MediaPartId);
        }
    }

    public void PurgePart(Guid mediaPartId, string transcodeTempDirectory) =>
        _extractor.PurgePart(transcodeTempDirectory, mediaPartId);

    private async Task<bool> IsEnabledAsync() => (await _settingsRepo.GetSettingsAsync()).PreExtractSubtitlesOnScan;

    private async Task RunAsync(IReadOnlyList<SubtitleExtractionTargetDto> targets, CancellationToken cancellationToken)
    {
        var root = await ResolveCacheDirectoryRootAsync();

        // The ordinal a retry needs is the track's position among ALL of the
        // file's subtitles in stream order, which is how ffmpeg numbers 0:s:N.
        // Tracks arrive ordered by StreamIndex, so the index is taken before the
        // image ones are filtered out — indexing the filtered list would point
        // the retry at a different track.
        var work = targets
            .SelectMany(t => t.Tracks
                .Select((track, ordinal) => (Target: t, Track: track, Ordinal: ordinal))
                .Where(x => IsExtractableSubtitleCodec(x.Track.Codec)))
            .ToList();

        if (work.Count == 0) return;

        var pending = work
            .Where(w => !_extractor.HasValidCachedWebVtt(root, w.Target.MediaPartId, w.Track.Id, w.Target.FilePath, w.Track.StreamIndex))
            .ToList();

        if (pending.Count == 0) return;

        var done = 0;
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForIdleTranscodersAsync(cancellationToken);

            _progress.Report($"Pre-extracting subtitles ({++done}/{pending.Count})");

            try
            {
                var produced = await _extractor.GetOrExtractWebVttAsync(
                    item.Target.FilePath, item.Track.StreamIndex, item.Ordinal, root,
                    item.Target.MediaPartId, item.Track.Id, cancellationToken);

                if (produced == null)
                {
                    _logger.LogWarning("Subtitle pre-extraction produced nothing for part {PartId} track {TrackId} ({FilePath}).",
                        item.Target.MediaPartId, item.Track.Id, item.Target.FilePath);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subtitle pre-extraction failed for part {PartId} track {TrackId}.",
                    item.Target.MediaPartId, item.Track.Id);
            }
        }

        _progress.Report(null);
    }

    private async Task WaitForIdleTranscodersAsync(CancellationToken cancellationToken)
    {
        if (_transcodeService.GetActiveTranscodeCount() == 0) return;

        var deadline = DateTime.UtcNow + MaxTranscodeWait;
        while (_transcodeService.GetActiveTranscodeCount() > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogInformation("Subtitle pre-extraction proceeding after waiting {Minutes} minutes for transcoders to go idle.",
                    MaxTranscodeWait.TotalMinutes);
                return;
            }

            _progress.Report("Pre-extracting subtitles (waiting for playback to finish)…");
            await Task.Delay(TranscodeBackoff, cancellationToken);
        }
    }
}
