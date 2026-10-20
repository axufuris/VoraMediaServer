using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Analysis;

public interface IPictureAreaManager
{
    Task DetectForItemAsync(Guid mediaItemId, CancellationToken cancellationToken = default);
    Task DetectForLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default);
}

public class PictureAreaManager : IPictureAreaManager
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IPictureAreaDetector _detector;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<PictureAreaManager> _logger;

    public PictureAreaManager(IMediaRepository mediaRepository, IPictureAreaDetector detector, ITaskProgressReporter progress, ILogger<PictureAreaManager> logger)
    {
        _mediaRepository = mediaRepository;
        _detector = detector;
        _progress = progress;
        _logger = logger;
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
            _progress.Report($"Finding black bars ({++done}/{targets.Count})");

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
}
