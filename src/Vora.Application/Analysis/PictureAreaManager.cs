using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Streaming;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Analysis;

public interface IPictureAreaManager
{
    Task DetectForItemAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task DetectForLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);
}

public class PictureAreaManager : IPictureAreaManager
{
    private static readonly TimeSpan PlaybackBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LongestWaitForPlayback = TimeSpan.FromMinutes(30);

    private readonly IMediaRepository _mediaRepository;
    private readonly IPictureAreaDetector _detector;
    private readonly ITranscodeService _transcodes;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<PictureAreaManager> _logger;
    private readonly TimeSpan _backoff;

    public PictureAreaManager(IMediaRepository mediaRepository, IPictureAreaDetector detector, ITranscodeService transcodes, ITaskProgressReporter progress, ILogger<PictureAreaManager> logger)
        : this(mediaRepository, detector, transcodes, progress, logger, PlaybackBackoff)
    {
    }

    internal PictureAreaManager(IMediaRepository mediaRepository, IPictureAreaDetector detector, ITranscodeService transcodes, ITaskProgressReporter progress, ILogger<PictureAreaManager> logger, TimeSpan backoff)
    {
        _mediaRepository = mediaRepository;
        _detector = detector;
        _transcodes = transcodes;
        _progress = progress;
        _logger = logger;
        _backoff = backoff;
    }

    public async Task DetectForItemAsync(Guid mediaItemId, CancellationToken cancellationToken = default) =>
        await RunAsync(await _mediaRepository.GetPictureAreaTargetsForItemAsync(mediaItemId), cancellationToken);

    public async Task DetectForLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        await RunAsync(await _mediaRepository.GetPictureAreaTargetsForLibraryAsync(libraryId), cancellationToken);

    private async Task RunAsync(IReadOnlyList<PictureAreaTarget> targets, CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;
            await WaitForPlaybackToFinishAsync(done, targets.Count, cancellationToken);
            _progress.Report($"Finding black bars ({done}/{targets.Count})");

            PictureCrop? crop;
            try
            {
                crop = await _detector.DetectAsync(target, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not look for black bars in {FilePath}.", target.FilePath);
                continue;
            }

            if (crop == null)
            {
                _logger.LogWarning("Could not look for black bars in {FilePath}; it is tried again on its next pass.", target.FilePath);
                continue;
            }

            await _mediaRepository.SavePictureCropAsync(target.VideoTrackId, crop);
            if (!crop.IsNone)
            {
                _logger.LogInformation("{FilePath} has black bars: {Top} px top, {Bottom} px bottom, {Left} px left, {Right} px right.",
                    target.FilePath, crop.Top, crop.Bottom, crop.Left, crop.Right);
            }
        }

        if (targets.Count > 0) _progress.Report(null);
    }

    private async Task WaitForPlaybackToFinishAsync(int done, int total, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + LongestWaitForPlayback;
        while (_transcodes.GetActiveTranscodeCount() > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogInformation("Finding black bars goes on after waiting {Minutes} minutes for playback to finish.", LongestWaitForPlayback.TotalMinutes);
                return;
            }

            _progress.Report($"Finding black bars ({done}/{total}, waiting for playback to finish)");
            await Task.Delay(_backoff, cancellationToken);
        }
    }
}
