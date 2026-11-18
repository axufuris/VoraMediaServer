using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Enums;
using Vora.Plugins;
using Vora.Plugins.Dtos;

namespace Vora.Application.Webhooks;

public interface IMediaWebhookQueue
{
    void Enqueue(MediaWebhookWork work);
}

public sealed class MediaWebhookProcessor : BackgroundService, IMediaWebhookQueue
{
    private const int MaxConcurrentBatches = 8;

    private readonly Channel<MediaWebhookWork> _channel = Channel.CreateUnbounded<MediaWebhookWork>();
    private readonly ConcurrentDictionary<string, DateTime> _recentlyQueued = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMediaWebhookLog _log;
    private readonly MediaWebhookTimings _timings;
    private readonly ILogger<MediaWebhookProcessor> _logger;

    public MediaWebhookProcessor(IServiceScopeFactory scopeFactory, IMediaWebhookLog log, MediaWebhookTimings timings, ILogger<MediaWebhookProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _timings = timings;
        _logger = logger;
    }

    public void Enqueue(MediaWebhookWork work) => _channel.Writer.TryWrite(work);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentBatches);

        try
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await gate.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessAsync(work, stoppingToken);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task ProcessAsync(MediaWebhookWork work, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var settingsRepo = services.GetRequiredService<ISystemSettingsRepository>();
            var libraryRepo = services.GetRequiredService<ILibraryRepository>();
            var mediaRepo = services.GetRequiredService<IMediaRepository>();
            var taskQueue = services.GetRequiredService<ITaskQueueManager>();

            var settings = await settingsRepo.GetSettingsAsync();
            var mappings = MediaWebhookPathMappings.Parse(await settingsRepo.GetPluginSettingAsync(work.ProviderId, MediaWebhookSettingKeys.PathMappings));
            var libraries = (await libraryRepo.GetAllProjectedAsync(l => new MediaWebhookLibrary(l.Id, l.Name, l.Type, l.FolderPaths, l.ExcludeFilters)))
                .Where(l => l.Type == work.LibraryType)
                .Select(l => l with { ExcludeFilters = LibraryFileFilter.Combine(l.ExcludeFilters, settings.ScanIgnoredFolders) })
                .ToList();

            var resolver = new MediaWebhookPathResolver(mappings, libraries, async top =>
                Directory.Exists(top) || File.Exists(top) || await mediaRepo.AnyMediaUnderPathAsync(top));

            foreach (var change in work.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessChangeAsync(work, change, resolver, taskQueue, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handling a {Event} webhook from {Provider} failed.", work.EventName, work.ProviderId);
            _log.Record(work.ProviderId, work.InstanceName, work.EventName, string.Empty, $"Failed: {ex.Message}", false);
        }
    }

    private async Task ProcessChangeAsync(MediaWebhookWork work, MediaWebhookChange change, MediaWebhookPathResolver resolver, ITaskQueueManager taskQueue, CancellationToken cancellationToken)
    {
        var resolution = await resolver.ResolveAsync(change.Path);
        if (resolution.Target is not { } target)
        {
            Record(work, change.Path, resolution.Reason, false);
            _logger.LogWarning("Webhook {Event} from {Provider} named {Path}, which Vora could not place: {Reason}", work.EventName, work.ProviderId, change.Path, resolution.Reason);
            return;
        }

        switch (change.Kind)
        {
            case MediaWebhookChangeKind.Added:
                await AddFileAsync(work, target, taskQueue, cancellationToken);
                break;
            case MediaWebhookChangeKind.Removed:
                await RemoveFileAsync(work, target, taskQueue, cancellationToken);
                break;
            case MediaWebhookChangeKind.FolderRemoved:
                await RemoveFolderAsync(work, target, taskQueue, cancellationToken);
                break;
        }
    }

    private async Task AddFileAsync(MediaWebhookWork work, MediaWebhookTarget target, ITaskQueueManager taskQueue, CancellationToken cancellationToken)
    {
        var path = target.Path;

        if (!LibraryMediaFiles.IsSupportedFor(path, target.Library.Type))
        {
            Record(work, path, "Skipped: not a media file this library holds.", true);
            return;
        }

        if (LibraryFileFilter.IsExcluded(path, target.Library.ExcludeFilters))
        {
            Record(work, path, "Skipped: the library excludes this file.", true);
            return;
        }

        if (!await WaitForFileAsync(path, cancellationToken))
        {
            Record(work, path, $"Not found on disk after {_timings.AddRetryDelays.Count + 1} checks. Check the path mapping, and that Vora can see the folder.", false);
            _logger.LogWarning("Webhook {Event} from {Provider} reported {Path}, which never appeared on disk.", work.EventName, work.ProviderId, path);
            return;
        }

        if (AlreadyQueued(path))
        {
            Record(work, path, "Already queued a moment ago.", true);
            return;
        }

        if (target.Library.Type == LibraryType.Music)
        {
            taskQueue.QueueScanNewMusicFile(target.Library.Id, path);
        }
        else
        {
            taskQueue.QueueScanNewFile(target.Library.Id, path);
        }

        Record(work, path, $"Queued for {target.Library.Name}.", true);
    }

    private async Task RemoveFileAsync(MediaWebhookWork work, MediaWebhookTarget target, ITaskQueueManager taskQueue, CancellationToken cancellationToken)
    {
        var path = target.Path;

        if (!LibraryMediaFiles.IsSupportedFor(path, target.Library.Type))
        {
            Record(work, path, "Skipped: not a media file this library holds.", true);
            return;
        }

        await Task.Delay(_timings.RemoveSettle, cancellationToken);

        if (File.Exists(path))
        {
            Record(work, path, "Kept: the file is still on disk.", true);
            return;
        }

        if (!Directory.Exists(target.Root))
        {
            Record(work, path, $"Kept: the library folder {target.Root} cannot be reached, so Vora cannot tell the file was deleted.", false);
            return;
        }

        taskQueue.QueueRemoveOrphanedMedia(path);
        Record(work, path, "Gone from disk. Moved to Media Trash.", true);
    }

    private async Task RemoveFolderAsync(MediaWebhookWork work, MediaWebhookTarget target, ITaskQueueManager taskQueue, CancellationToken cancellationToken)
    {
        var path = target.Path;

        if (MediaWebhookPathResolver.Segments(path).Length <= MediaWebhookPathResolver.Segments(target.Root).Length)
        {
            Record(work, path, "Refused: that is a whole library folder, not a title inside one.", false);
            return;
        }

        await Task.Delay(_timings.RemoveSettle, cancellationToken);

        if (!Directory.Exists(target.Root))
        {
            Record(work, path, $"Kept: the library folder {target.Root} cannot be reached, so Vora cannot tell the files were deleted.", false);
            return;
        }

        taskQueue.QueueRemoveMissingMediaUnder(path);
        Record(work, path, Directory.Exists(path)
            ? "The folder is still there: only files that are gone from it will move to Media Trash."
            : "Folder gone from disk. Its files will move to Media Trash.", true);
    }

    private async Task<bool> WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) return true;

        foreach (var delay in _timings.AddRetryDelays)
        {
            await Task.Delay(delay, cancellationToken);
            if (File.Exists(path)) return true;
        }

        return false;
    }

    private bool AlreadyQueued(string path)
    {
        var now = DateTime.UtcNow;
        foreach (var stale in _recentlyQueued.Where(e => now - e.Value > _timings.DuplicateWindow).Select(e => e.Key).ToList())
        {
            _recentlyQueued.TryRemove(stale, out _);
        }

        return !_recentlyQueued.TryAdd(path, now);
    }

    private void Record(MediaWebhookWork work, string path, string outcome, bool ok) =>
        _log.Record(work.ProviderId, work.InstanceName, work.EventName, path, outcome, ok);
}
