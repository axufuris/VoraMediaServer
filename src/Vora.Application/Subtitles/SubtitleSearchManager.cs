using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Subtitles.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Subtitles;

public interface ISubtitleSearchManager
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubtitleSearchResultVM>> SearchAsync(Guid mediaItemId, IReadOnlyList<string> languages, CancellationToken cancellationToken = default);
    Task<DownloadedSubtitleVM?> DownloadAndAttachAsync(Guid mediaItemId, string providerFileId, string? language, CancellationToken cancellationToken = default);
}

public class SubtitleSearchManager : ISubtitleSearchManager
{
    private readonly IEnumerable<ISubtitleSearchProvider> _providers;
    private readonly IMediaRepository _mediaRepository;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ISubtitleExtractionService _extractor;
    private readonly StoragePathsOptions _storagePaths;
    private readonly ILogger<SubtitleSearchManager> _logger;

    public SubtitleSearchManager(
        IEnumerable<ISubtitleSearchProvider> providers,
        IMediaRepository mediaRepository,
        ISystemSettingsRepository settingsRepo,
        ISubtitleExtractionService extractor,
        IOptions<StoragePathsOptions> storagePaths,
        ILogger<SubtitleSearchManager> logger)
    {
        _providers = providers;
        _mediaRepository = mediaRepository;
        _settingsRepo = settingsRepo;
        _extractor = extractor;
        _storagePaths = storagePaths.Value;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await ResolveProviderAsync(cancellationToken) != null;

    public async Task<IReadOnlyList<SubtitleSearchResultVM>> SearchAsync(Guid mediaItemId, IReadOnlyList<string> languages, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);
        if (provider == null) return Array.Empty<SubtitleSearchResultVM>();

        var query = await BuildQueryAsync(mediaItemId, languages);
        if (query == null) return Array.Empty<SubtitleSearchResultVM>();

        var results = await provider.SearchAsync(query, cancellationToken);

        return results.Select(r => new SubtitleSearchResultVM
        {
            ProviderId = r.ProviderId,
            ProviderFileId = r.ProviderFileId,
            ReleaseName = r.ReleaseName,
            Language = r.Language,
            Format = r.Format,
            HearingImpaired = r.HearingImpaired,
            Forced = r.Forced,
            DownloadCount = r.DownloadCount,
            Uploader = r.Uploader,
            Rating = r.Rating,
        }).ToList();
    }

    public async Task<DownloadedSubtitleVM?> DownloadAndAttachAsync(Guid mediaItemId, string providerFileId, string? language, CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(cancellationToken);
        if (provider == null) return null;

        var partId = await _mediaRepository.GetPrimaryMediaPartIdAsync(mediaItemId);
        if (partId == null) return null;

        var download = await provider.DownloadAsync(providerFileId, cancellationToken);
        if (download == null || download.Content.Length == 0) return null;

        var trackId = Guid.NewGuid();
        var storePath = BuildStorePath(mediaItemId, trackId);
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        // The provider hands back whatever format it holds. It is converted once,
        // here, so the file in the store is already what the delivery endpoint
        // serves — the per-request path then never runs FFmpeg for it at all.
        var stagedSource = storePath + ".src";
        try
        {
            await File.WriteAllBytesAsync(stagedSource, download.Content, cancellationToken);

            if (!await _extractor.ConvertToWebVttAsync(stagedSource, storePath, cancellationToken))
            {
                _logger.LogWarning("Could not convert the downloaded subtitle {FileId} for media {MediaItemId} to WebVTT.", providerFileId, mediaItemId);
                return null;
            }
        }
        finally
        {
            TryDelete(stagedSource);
        }

        var resolvedLanguage = language ?? download.Language;

        var track = new MediaSubtitleTrack
        {
            Id = trackId,
            MediaPartId = partId.Value,
            ExternalFilePath = storePath,
            IsDownloaded = true,
            Codec = "webvtt",
            Language = resolvedLanguage,
            Title = BuildTitle(resolvedLanguage, provider.Name),
        };

        await _mediaRepository.AddSubtitleTrackAsync(track);

        _logger.LogInformation("Downloaded subtitle {FileId} from {Provider} for media {MediaItemId} into {Path}.",
            providerFileId, provider.Id, mediaItemId, storePath);

        return new DownloadedSubtitleVM
        {
            Id = track.Id,
            MediaPartId = track.MediaPartId,
            Language = track.Language,
            Title = track.Title,
            Codec = track.Codec,
            IsForced = track.IsForced,
            IsDefault = track.IsDefault,
        };
    }

    // A track fetched from a provider is not "English" the way an embedded track
    // is — naming the source is what lets a viewer tell two of them apart, and
    // tell either from the one that shipped in the file.
    public static string BuildTitle(string? language, string providerName) =>
        string.IsNullOrWhiteSpace(language) ? providerName : $"{language.ToUpperInvariant()} ({providerName})";

    private async Task<ISubtitleSearchProvider?> ResolveProviderAsync(CancellationToken cancellationToken)
    {
        var installed = _providers.ToList();
        if (installed.Count == 0) return null;

        var configuredId = (await _settingsRepo.GetSettingsAsync()).SubtitleSearchProviderId;

        // A named provider is honoured only while it is actually usable: an admin
        // who picks one and never enters a key must not hide a second provider
        // that is ready to work.
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            var chosen = installed.FirstOrDefault(p => p.Id == configuredId);
            if (chosen != null && await IsUsableAsync(chosen, cancellationToken)) return chosen;
        }

        foreach (var provider in installed)
        {
            if (await IsUsableAsync(provider, cancellationToken)) return provider;
        }

        return null;
    }

    private async Task<bool> IsUsableAsync(ISubtitleSearchProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.IsConfiguredAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subtitle provider {ProviderId} threw while reporting whether it is configured.", provider.Id);
            return false;
        }
    }

    private async Task<SubtitleSearchQuery?> BuildQueryAsync(Guid mediaItemId, IReadOnlyList<string> languages)
    {
        var facts = await _mediaRepository.GetSubtitleSearchFactsAsync(mediaItemId);
        if (facts == null) return null;

        return new SubtitleSearchQuery
        {
            Title = facts.SeriesTitle ?? facts.Title,
            Year = facts.Year,
            ImdbId = facts.ImdbId,
            TmdbId = facts.TmdbId,
            Season = facts.SeasonNumber,
            Episode = facts.EpisodeNumber,
            Languages = languages,
        };
    }

    private string BuildStorePath(Guid mediaItemId, Guid trackId)
    {
        var root = string.IsNullOrWhiteSpace(_storagePaths.Subtitles)
            ? Path.Combine(AppContext.BaseDirectory, "subtitles")
            : _storagePaths.Subtitles;

        var shard = mediaItemId.ToString("N")[..2];
        return Path.Combine(root, shard, mediaItemId.ToString("N"), $"{trackId:N}.vtt");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
