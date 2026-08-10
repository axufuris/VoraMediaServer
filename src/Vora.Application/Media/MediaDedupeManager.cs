using Microsoft.Extensions.Logging;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public interface IMediaDedupeManager
{
    Task<List<DedupeGroupVM>> GetDuplicateMediaAsync();
    Task DeleteDuplicatePartAsync(Guid partId, bool deletePhysicalFile);
    Task<TvShowMergeResultVM> MergeDuplicateTvShowsAsync(Guid? libraryId = null);

    Task<DedupeSettingsVM> GetGlobalSettingsAsync();
    Task<DedupeSettingsVM> GetEffectiveLibrarySettingsAsync(Guid libraryId);
    Task<List<DedupeSettingsVM>> GetAllLibraryOverridesAsync();
    Task<DedupeSettingsVM> SaveGlobalSettingsAsync(DedupeSettingsVM settings);
    Task<DedupeSettingsVM> SaveLibraryOverrideAsync(Guid libraryId, DedupeSettingsVM settings);
    Task ClearLibraryOverrideAsync(Guid libraryId);
    Task<DedupeSettingsVM> GetDefaultSettingsAsync();

    Task<List<DedupeIgnoredGroupVM>> GetIgnoredGroupsAsync();
    Task IgnoreGroupAsync(Guid mediaItemId, string resolution, string? profileId, string? note);
    Task UnignoreGroupAsync(Guid ignoredGroupId);
}

public class MediaDedupeManager : IMediaDedupeManager
{
    private readonly IMediaDedupeRepository _repository;
    private readonly IMediaRepository _mediaRepository;
    private readonly ILogger<MediaDedupeManager> _logger;

    public MediaDedupeManager(IMediaDedupeRepository repository, IMediaRepository mediaRepository, ILogger<MediaDedupeManager> logger)
    {
        _repository = repository;
        _mediaRepository = mediaRepository;
        _logger = logger;
    }

    public async Task<TvShowMergeResultVM> MergeDuplicateTvShowsAsync(Guid? libraryId = null)
    {
        var result = await _repository.MergeDuplicateTvShowsAsync(libraryId);

        foreach (var episodeId in result.AffectedEpisodeIds.Distinct())
        {
            await _mediaRepository.SyncItemEditionFromPartsAsync(episodeId);
        }

        if (result.ShowsRemoved > 0)
        {
            _logger.LogInformation(
                "Merged duplicate TV shows: {Groups} group(s), removed {Shows} duplicate show row(s), moved {Parts} file part(s).",
                result.GroupsMerged, result.ShowsRemoved, result.PartsMoved);
        }

        return result;
    }

    public async Task<List<DedupeGroupVM>> GetDuplicateMediaAsync()
    {
        var allItems = await _repository.GetMediaItemsWithMultiplePartsAsync();
        var globalSettings = await LoadOrCreateDefaultGlobalAsync();
        var libraryOverrides = await _repository.GetAllLibraryOverridesAsync();
        var libraryMap = libraryOverrides.ToDictionary(o => o.LibraryId!.Value, o => o);

        var ignored = await _repository.GetIgnoredGroupsAsync();
        var ignoredKeys = new HashSet<string>(ignored.Select(i => BuildIgnoreKey(i.MediaItemId, i.Resolution)));

        var result = new List<DedupeGroupVM>();

        foreach (var item in allItems)
        {
            var settings = libraryMap.TryGetValue(item.LibraryId, out var overrideEntity)
                ? overrideEntity
                : globalSettings;

            var eligibleParts = item.MediaParts
                .Where(p => PassesThresholds(p, settings))
                .ToList();

            if (eligibleParts.Count < 2) continue;

            if (item is Track track)
            {
                BuildAudioGroup(track, eligibleParts, settings, ignoredKeys, result);
            }
            else
            {
                BuildVideoGroups(item, eligibleParts, settings, ignoredKeys, result);
            }
        }

        return result.OrderBy(r => r.Title).ToList();
    }

    private void BuildVideoGroups(MediaItem item, List<MediaPart> eligibleParts, MediaDedupeSettings settings, HashSet<string> ignoredKeys, List<DedupeGroupVM> result)
    {
        var resolutionGroups = settings.GroupAcrossResolutions
            ? new[] { new ResolutionGrouping(string.Empty, eligibleParts) }
            : eligibleParts
                .GroupBy(p => NormalizeResolution(p.Resolution))
                .Select(g => new ResolutionGrouping(g.Key, g.ToList()))
                .ToArray();

        foreach (var resolutionGroup in resolutionGroups)
        {
            var runtimeBuckets = settings.RuntimeToleranceSeconds > 0
                ? GroupByRuntimeTolerance(resolutionGroup.Parts, settings.RuntimeToleranceSeconds)
                : new List<List<MediaPart>> { resolutionGroup.Parts };

            foreach (var bucket in runtimeBuckets)
            {
                if (bucket.Count < 2) continue;

                var groupResolutionLabel = settings.GroupAcrossResolutions
                    ? "ALL"
                    : (string.IsNullOrEmpty(resolutionGroup.Key) ? "UNKNOWN" : resolutionGroup.Key.ToUpperInvariant());

                if (ignoredKeys.Contains(BuildIgnoreKey(item.Id, groupResolutionLabel)))
                {
                    continue;
                }

                result.Add(new DedupeGroupVM
                {
                    MediaItemId = item.Id,
                    Title = BuildTitle(item),
                    Type = item is Movie ? "Movie" : "Episode",
                    MediaKind = "video",
                    Resolution = groupResolutionLabel,
                    Parts = bucket.Select(p => MapAndScoreVideoPart(p, settings))
                        .OrderByDescending(p => p.QualityScore)
                        .ToList()
                });
            }
        }
    }

    private void BuildAudioGroup(Track track, List<MediaPart> eligibleParts, MediaDedupeSettings settings, HashSet<string> ignoredKeys, List<DedupeGroupVM> result)
    {
        var groupResolutionLabel = "ALL";

        if (ignoredKeys.Contains(BuildIgnoreKey(track.Id, groupResolutionLabel)))
        {
            return;
        }

        result.Add(new DedupeGroupVM
        {
            MediaItemId = track.Id,
            Title = BuildTitle(track),
            Type = "Track",
            MediaKind = "audio",
            Resolution = groupResolutionLabel,
            Parts = eligibleParts.Select(p => MapAndScoreAudioPart(p, track, settings))
                .OrderByDescending(p => p.QualityScore)
                .ToList()
        });
    }

    public async Task DeleteDuplicatePartAsync(Guid partId, bool deletePhysicalFile)
    {
        var part = await _repository.GetMediaPartByIdAsync(partId);
        if (part == null) throw new InvalidOperationException("Part not found.");

        if (deletePhysicalFile && !string.IsNullOrWhiteSpace(part.FilePath))
        {
            try
            {
                if (File.Exists(part.FilePath))
                {
                    File.Delete(part.FilePath);
                    _logger.LogInformation("Physically deleted duplicate file: {Path}", part.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to physically delete file: {Path}", part.FilePath);
                throw new InvalidOperationException("Could not delete physical file. It may be locked by another process.");
            }
        }

        await _repository.DeleteMediaPartAsync(part);
    }

    public async Task<DedupeSettingsVM> GetGlobalSettingsAsync()
    {
        var entity = await LoadOrCreateDefaultGlobalAsync();
        return ToVM(entity);
    }

    public async Task<DedupeSettingsVM> GetEffectiveLibrarySettingsAsync(Guid libraryId)
    {
        var overrideEntity = await _repository.GetLibraryOverrideAsync(libraryId);
        if (overrideEntity != null)
        {
            return ToVM(overrideEntity);
        }

        var global = await LoadOrCreateDefaultGlobalAsync();
        var vm = ToVM(global);
        vm.LibraryId = libraryId;
        vm.IsDefault = true;
        return vm;
    }

    public async Task<List<DedupeSettingsVM>> GetAllLibraryOverridesAsync()
    {
        var entities = await _repository.GetAllLibraryOverridesAsync();
        return entities.Select(ToVM).ToList();
    }

    public async Task<DedupeSettingsVM> SaveGlobalSettingsAsync(DedupeSettingsVM settings)
    {
        var entity = FromVM(settings, libraryId: null);
        var saved = await _repository.UpsertSettingsAsync(entity);
        return ToVM(saved);
    }

    public async Task<DedupeSettingsVM> SaveLibraryOverrideAsync(Guid libraryId, DedupeSettingsVM settings)
    {
        var entity = FromVM(settings, libraryId);
        var saved = await _repository.UpsertSettingsAsync(entity);
        return ToVM(saved);
    }

    public async Task ClearLibraryOverrideAsync(Guid libraryId)
    {
        await _repository.DeleteLibraryOverrideAsync(libraryId);
    }

    public Task<DedupeSettingsVM> GetDefaultSettingsAsync()
    {
        return Task.FromResult(ToVM(BuildDefaultEntity()));
    }

    public async Task<List<DedupeIgnoredGroupVM>> GetIgnoredGroupsAsync()
    {
        var entities = await _repository.GetIgnoredGroupsAsync();
        var result = new List<DedupeIgnoredGroupVM>();
        foreach (var ig in entities)
        {
            var title = ig.MediaItem != null ? BuildTitle(ig.MediaItem) : "(missing media item)";
            var type = ig.MediaItem switch
            {
                Movie => "Movie",
                Episode => "Episode",
                Track => "Track",
                _ => "Unknown"
            };
            result.Add(new DedupeIgnoredGroupVM
            {
                Id = ig.Id,
                MediaItemId = ig.MediaItemId,
                Title = title,
                Type = type,
                Resolution = ig.Resolution,
                IgnoredAt = ig.IgnoredAt,
                Note = ig.Note
            });
        }
        return result;
    }

    public async Task IgnoreGroupAsync(Guid mediaItemId, string resolution, string? profileId, string? note)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            throw new ArgumentException("Resolution is required.", nameof(resolution));
        }

        var normalizedResolution = resolution.Trim().ToUpperInvariant();
        var existing = await _repository.GetIgnoredGroupAsync(mediaItemId, normalizedResolution);
        if (existing != null) return;

        await _repository.AddIgnoredGroupAsync(new MediaDedupeIgnoredGroup
        {
            MediaItemId = mediaItemId,
            Resolution = normalizedResolution,
            IgnoredByProfileId = profileId,
            Note = note
        });
    }

    public async Task UnignoreGroupAsync(Guid ignoredGroupId)
    {
        await _repository.RemoveIgnoredGroupAsync(ignoredGroupId);
    }

    private async Task<MediaDedupeSettings> LoadOrCreateDefaultGlobalAsync()
    {
        var existing = await _repository.GetGlobalSettingsAsync();
        if (existing != null) return existing;

        var defaults = BuildDefaultEntity();
        var saved = await _repository.UpsertSettingsAsync(defaults);
        return saved;
    }

    private static MediaDedupeSettings BuildDefaultEntity()
    {
        return new MediaDedupeSettings { LibraryId = null };
    }

    private DedupeItemVM MapAndScoreVideoPart(MediaPart part, MediaDedupeSettings settings)
    {
        var video = part.VideoTracks.FirstOrDefault();
        long score = 0;

        var res = part.Resolution?.ToLowerInvariant() ?? string.Empty;
        if (res.Contains("4k") || res.Contains("2160")) score += settings.ScoreResolution4k;
        else if (res.Contains("1080")) score += settings.ScoreResolution1080;
        else if (res.Contains("720")) score += settings.ScoreResolution720;
        else score += settings.ScoreResolutionOther;

        var (sourceLabel, sourceScore) = DetectSource(part.FilePath, settings);
        score += sourceScore;

        var codec = video?.Codec?.ToLowerInvariant() ?? string.Empty;
        if (codec.Contains("av1")) score += settings.ScoreCodecAv1;
        else if (codec.Contains("hevc") || codec.Contains("h265") || codec.Contains("x265")) score += settings.ScoreCodecHevc;
        else if (codec.Contains("vp9")) score += settings.ScoreCodecVp9;
        else if (codec.Contains("h264") || codec.Contains("x264") || codec.Contains("avc")) score += settings.ScoreCodecH264;

        var (hdrPoints, hdrLabel) = ScoreHdr(video?.HdrType, part.FilePath, settings);
        score += hdrPoints;

        long maxAudioScore = 0;
        var audioDescriptions = new List<string>();

        foreach (var audio in part.AudioTracks)
        {
            long currentAudio;
            var acodec = audio.Codec?.ToLowerInvariant() ?? string.Empty;
            var atitle = audio.Title?.ToLowerInvariant() ?? string.Empty;
            int channels = audio.Channels ?? 2;

            audioDescriptions.Add($"{acodec.ToUpperInvariant()} {channels}ch {(string.IsNullOrWhiteSpace(atitle) ? string.Empty : $"({atitle})")}");

            if (acodec.Contains("truehd") || acodec.Contains("dts-hd") || atitle.Contains("atmos")) currentAudio = settings.ScoreAudioLossless;
            else if (acodec.Contains("eac3") || acodec.Contains("ac3") || channels >= 6) currentAudio = settings.ScoreAudioSurround;
            else currentAudio = settings.ScoreAudioBase;

            if (currentAudio > maxAudioScore) maxAudioScore = currentAudio;
        }
        score += maxAudioScore;

        if (part.OverallBitrate.HasValue && part.OverallBitrate > 0 && settings.ScoreBitrateDivisor > 0)
        {
            score += part.OverallBitrate.Value / settings.ScoreBitrateDivisor;
        }

        return new DedupeItemVM
        {
            PartId = part.Id,
            FilePath = part.FilePath,
            FileName = Path.GetFileName(part.FilePath),
            FileSizeBytes = part.FileSizeBytes ?? 0,
            VideoCodec = codec.ToUpperInvariant(),
            Source = sourceLabel,
            HdrFormat = hdrLabel,
            AudioTracks = audioDescriptions,
            QualityScore = score,
            Container = part.Container?.ToUpperInvariant() ?? "UNKNOWN",
            Bitrate = part.OverallBitrate
        };
    }

    internal static (long Points, string Label) ScoreHdr(string? hdrType, string? filePath, MediaDedupeSettings settings)
    {
        var hdr = hdrType?.ToLowerInvariant() ?? string.Empty;
        var isDolbyVision = hdr.Contains("dovi") || hdr.Contains("dolby vision");
        var isBaseHdr = isDolbyVision || hdr.Contains("hdr");

        long points = 0;
        if (isDolbyVision) points += settings.ScoreHdrDolbyVision;
        else if (hdr.Contains("hdr")) points += settings.ScoreHdr;

        var isHdr10Plus = isBaseHdr && (hdr.Contains("hdr10plus") || hdr.Contains("hdr10+") || FileNameSignalsHdr10Plus(filePath));
        if (isHdr10Plus) points += settings.ScoreHdr10PlusBonus;

        if (!isBaseHdr) return (points, "SDR");

        var label = (hdrType ?? string.Empty).ToUpperInvariant();
        if (isHdr10Plus && !label.Contains("PLUS") && !label.Contains("+"))
        {
            label = label.Contains("HDR10") ? label.Replace("HDR10", "HDR10+") : $"{label}/HDR10+";
        }

        return (points, label);
    }

    private static bool FileNameSignalsHdr10Plus(string? filePath)
    {
        var name = Path.GetFileName(filePath)?.ToLowerInvariant() ?? string.Empty;
        return name.Contains("hdr10plus") || name.Contains("hdr10+") || name.Contains("hdr10 plus");
    }

    private static (string? Label, int Score) DetectSource(string filePath, MediaDedupeSettings settings)
    {
        var name = Path.GetFileName(filePath)?.ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0) return (null, 0);

        if (name.Contains("remux")) return ("REMUX", settings.ScoreSourceRemux);
        if (name.Contains("bluray") || name.Contains("blu-ray") || name.Contains("bdrip") || name.Contains("brrip"))
            return ("BLURAY", settings.ScoreSourceBluRay);
        if (name.Contains("webrip") || name.Contains("web-rip") || name.Contains("web.rip"))
            return ("WEBRIP", settings.ScoreSourceWebRip);
        if (name.Contains("web-dl") || name.Contains("webdl") || name.Contains("web.dl") || name.Contains("web "))
            return ("WEB-DL", settings.ScoreSourceWebDl);
        if (name.Contains("hdtv") || name.Contains("pdtv")) return ("HDTV", settings.ScoreSourceHdtv);
        if (name.Contains("dvdrip") || name.Contains("dvd")) return ("DVD", settings.ScoreSourceDvd);

        return (null, 0);
    }

    private DedupeItemVM MapAndScoreAudioPart(MediaPart part, Track track, MediaDedupeSettings settings)
    {
        long score = 0;

        var codec = (track.AudioCodec ?? part.Container ?? string.Empty).ToLowerInvariant();
        if (IsLosslessAudioCodec(codec)) score += settings.ScoreCodecMusicLossless;
        else if (IsLossyHighAudioCodec(codec)) score += settings.ScoreCodecMusicLossyHigh;
        else score += settings.ScoreCodecMusicLossyStandard;

        var sampleRate = track.SampleRate ?? 0;
        if (sampleRate >= 88200) score += settings.ScoreSampleRateHi;
        else if (sampleRate >= 44100) score += settings.ScoreSampleRateStandard;
        else score += settings.ScoreSampleRateLow;

        var bitrateBps = ResolveAudioBitrateBps(part, track);
        if (bitrateBps > 0 && settings.ScoreBitrateDivisor > 0)
        {
            score += bitrateBps / settings.ScoreBitrateDivisor;
        }

        var sizeBytes = part.FileSizeBytes ?? 0;
        if (sizeBytes > 0 && settings.ScoreFileSizeDivisor > 0)
        {
            score += sizeBytes / settings.ScoreFileSizeDivisor;
        }

        return new DedupeItemVM
        {
            PartId = part.Id,
            FilePath = part.FilePath,
            FileName = Path.GetFileName(part.FilePath),
            FileSizeBytes = sizeBytes,
            VideoCodec = string.Empty,
            HdrFormat = string.Empty,
            AudioCodec = string.IsNullOrWhiteSpace(codec) ? null : codec.ToUpperInvariant(),
            SampleRate = sampleRate > 0 ? sampleRate : null,
            AudioTracks = new List<string>(),
            QualityScore = score,
            Container = part.Container?.ToUpperInvariant() ?? "UNKNOWN",
            Bitrate = bitrateBps > 0 ? bitrateBps : null
        };
    }

    private static long ResolveAudioBitrateBps(MediaPart part, Track track)
    {
        if (part.OverallBitrate.HasValue && part.OverallBitrate > 0)
        {
            return part.OverallBitrate.Value;
        }
        if (track.Bitrate.HasValue && track.Bitrate > 0)
        {
            return (long)track.Bitrate.Value * 1000;
        }
        return 0;
    }

    private static bool IsLosslessAudioCodec(string codec)
    {
        return codec.Contains("flac")
            || codec.Contains("alac")
            || codec.Contains("wav")
            || codec.Contains("pcm")
            || codec.Contains("ape")
            || codec.Contains("dsd")
            || codec.Contains("dsf")
            || codec.Contains("wv");
    }

    private static bool IsLossyHighAudioCodec(string codec)
    {
        return codec.Contains("aac")
            || codec.Contains("m4a")
            || codec.Contains("opus");
    }

    private static bool PassesThresholds(MediaPart part, MediaDedupeSettings settings)
    {
        if (settings.MinimumFileSizeBytes > 0 && (part.FileSizeBytes ?? 0) < settings.MinimumFileSizeBytes)
        {
            return false;
        }

        if (settings.MinimumRuntimeSeconds > 0)
        {
            var runtime = part.Duration?.TotalSeconds ?? 0;
            if (runtime < settings.MinimumRuntimeSeconds) return false;
        }

        return true;
    }

    private static List<List<MediaPart>> GroupByRuntimeTolerance(List<MediaPart> parts, int toleranceSeconds)
    {
        var sorted = parts.OrderBy(p => p.Duration?.TotalSeconds ?? 0).ToList();
        var buckets = new List<List<MediaPart>>();
        List<MediaPart>? current = null;
        double currentAnchor = 0;

        foreach (var part in sorted)
        {
            var runtime = part.Duration?.TotalSeconds ?? 0;
            if (current == null || Math.Abs(runtime - currentAnchor) > toleranceSeconds)
            {
                current = new List<MediaPart>();
                buckets.Add(current);
                currentAnchor = runtime;
            }
            current.Add(part);
        }

        return buckets;
    }

    private static string NormalizeResolution(string? resolution)
    {
        return resolution?.ToLowerInvariant().Trim() ?? "unknown";
    }

    private static string BuildTitle(MediaItem item)
    {
        if (item is Episode ep && ep.Season?.TvShow != null)
        {
            return $"{ep.Season.TvShow.Title} - S{ep.Season.SeasonNumber:D2}E{ep.EpisodeNumber:D2} - {ep.Title}";
        }
        if (item is Track tr)
        {
            var artist = tr.Album?.Artist?.Name ?? tr.Artist;
            var album = tr.Album?.Title;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(artist)) parts.Add(artist!);
            if (!string.IsNullOrWhiteSpace(album)) parts.Add(album!);
            parts.Add(tr.Title);
            return string.Join(" - ", parts);
        }
        return item.Title;
    }

    private static string BuildIgnoreKey(Guid mediaItemId, string resolution)
    {
        return $"{mediaItemId}|{resolution.ToUpperInvariant()}";
    }

    private static DedupeSettingsVM ToVM(MediaDedupeSettings entity)
    {
        return new DedupeSettingsVM
        {
            LibraryId = entity.LibraryId,
            GroupAcrossResolutions = entity.GroupAcrossResolutions,
            RuntimeToleranceSeconds = entity.RuntimeToleranceSeconds,
            MinimumFileSizeBytes = entity.MinimumFileSizeBytes,
            MinimumRuntimeSeconds = entity.MinimumRuntimeSeconds,
            ScoreResolution4k = entity.ScoreResolution4k,
            ScoreResolution1080 = entity.ScoreResolution1080,
            ScoreResolution720 = entity.ScoreResolution720,
            ScoreResolutionOther = entity.ScoreResolutionOther,
            ScoreSourceRemux = entity.ScoreSourceRemux,
            ScoreSourceBluRay = entity.ScoreSourceBluRay,
            ScoreSourceWebDl = entity.ScoreSourceWebDl,
            ScoreSourceWebRip = entity.ScoreSourceWebRip,
            ScoreSourceHdtv = entity.ScoreSourceHdtv,
            ScoreSourceDvd = entity.ScoreSourceDvd,
            ScoreCodecAv1 = entity.ScoreCodecAv1,
            ScoreCodecHevc = entity.ScoreCodecHevc,
            ScoreCodecVp9 = entity.ScoreCodecVp9,
            ScoreCodecH264 = entity.ScoreCodecH264,
            ScoreHdrDolbyVision = entity.ScoreHdrDolbyVision,
            ScoreHdr = entity.ScoreHdr,
            ScoreHdr10PlusBonus = entity.ScoreHdr10PlusBonus,
            ScoreAudioLossless = entity.ScoreAudioLossless,
            ScoreAudioSurround = entity.ScoreAudioSurround,
            ScoreAudioBase = entity.ScoreAudioBase,
            ScoreBitrateDivisor = entity.ScoreBitrateDivisor,
            ScoreCodecMusicLossless = entity.ScoreCodecMusicLossless,
            ScoreCodecMusicLossyHigh = entity.ScoreCodecMusicLossyHigh,
            ScoreCodecMusicLossyStandard = entity.ScoreCodecMusicLossyStandard,
            ScoreSampleRateHi = entity.ScoreSampleRateHi,
            ScoreSampleRateStandard = entity.ScoreSampleRateStandard,
            ScoreSampleRateLow = entity.ScoreSampleRateLow,
            ScoreFileSizeDivisor = entity.ScoreFileSizeDivisor,
            IsDefault = false
        };
    }

    private static MediaDedupeSettings FromVM(DedupeSettingsVM vm, Guid? libraryId)
    {
        return new MediaDedupeSettings
        {
            LibraryId = libraryId,
            GroupAcrossResolutions = vm.GroupAcrossResolutions,
            RuntimeToleranceSeconds = Math.Max(0, vm.RuntimeToleranceSeconds),
            MinimumFileSizeBytes = Math.Max(0, vm.MinimumFileSizeBytes),
            MinimumRuntimeSeconds = Math.Max(0, vm.MinimumRuntimeSeconds),
            ScoreResolution4k = vm.ScoreResolution4k,
            ScoreResolution1080 = vm.ScoreResolution1080,
            ScoreResolution720 = vm.ScoreResolution720,
            ScoreResolutionOther = vm.ScoreResolutionOther,
            ScoreSourceRemux = vm.ScoreSourceRemux,
            ScoreSourceBluRay = vm.ScoreSourceBluRay,
            ScoreSourceWebDl = vm.ScoreSourceWebDl,
            ScoreSourceWebRip = vm.ScoreSourceWebRip,
            ScoreSourceHdtv = vm.ScoreSourceHdtv,
            ScoreSourceDvd = vm.ScoreSourceDvd,
            ScoreCodecAv1 = vm.ScoreCodecAv1,
            ScoreCodecHevc = vm.ScoreCodecHevc,
            ScoreCodecVp9 = vm.ScoreCodecVp9,
            ScoreCodecH264 = vm.ScoreCodecH264,
            ScoreHdrDolbyVision = vm.ScoreHdrDolbyVision,
            ScoreHdr = vm.ScoreHdr,
            ScoreHdr10PlusBonus = vm.ScoreHdr10PlusBonus,
            ScoreAudioLossless = vm.ScoreAudioLossless,
            ScoreAudioSurround = vm.ScoreAudioSurround,
            ScoreAudioBase = vm.ScoreAudioBase,
            ScoreBitrateDivisor = Math.Max(1, vm.ScoreBitrateDivisor),
            ScoreCodecMusicLossless = vm.ScoreCodecMusicLossless,
            ScoreCodecMusicLossyHigh = vm.ScoreCodecMusicLossyHigh,
            ScoreCodecMusicLossyStandard = vm.ScoreCodecMusicLossyStandard,
            ScoreSampleRateHi = vm.ScoreSampleRateHi,
            ScoreSampleRateStandard = vm.ScoreSampleRateStandard,
            ScoreSampleRateLow = vm.ScoreSampleRateLow,
            ScoreFileSizeDivisor = Math.Max(1L, vm.ScoreFileSizeDivisor)
        };
    }

    private sealed record ResolutionGrouping(string Key, List<MediaPart> Parts);
}
