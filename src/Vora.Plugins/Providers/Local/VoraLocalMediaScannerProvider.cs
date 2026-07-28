using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class VoraLocalMediaScannerProvider : ILocalMediaScannerProvider
{
    private readonly ILogger<VoraLocalMediaScannerProvider> _logger;
    private readonly IMediaIngestionService _ingestionService;
    private readonly ITaskProgressReporter _progress;
    private readonly string[] _supportedExtensions = { ".mkv", ".mp4", ".avi", ".m4v" };
    private readonly string[] _supportedAudioExtensions = { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aac", ".wma" };

    private static readonly string[] ExtraFileSuffixes =
    {
        "-trailer", "-sample", "-featurette", "-featurettes", "-behindthescenes",
        "-deleted", "-deletedscene", "-deletedscenes", "-interview", "-interviews",
        "-scene", "-scenes", "-short", "-shorts", "-clip", "-clips", "-other",
        "-extra", "-extras"
    };

    private static readonly HashSet<string> ExtraFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "trailers", "extras", "featurettes", "behind the scenes", "behindthescenes",
        "deleted scenes", "deletedscenes", "interviews", "scenes", "shorts",
        "clips", "samples", "sample", "other"
    };

    public string Id => "Vora_scanner";
    public string Name => "Vora Standard Scanner";
    public string Version => "1.0.0";
    public string Description => "The default high-performance local file scanner for Vora.";
    public bool IsSystemPlugin => true;
    public string Type => "LocalScanner";
    public string DeveloperName => "Andy Xufuris";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Enumerable.Empty<PluginSettingDefinitionDto>();

    public VoraLocalMediaScannerProvider(
        ILogger<VoraLocalMediaScannerProvider> logger,
        IMediaIngestionService ingestionService,
        ITaskProgressReporter progress)
    {
        _logger = logger;
        _ingestionService = ingestionService;
        _progress = progress;
    }

    public async Task ScanMovieLibraryAsync(Guid libraryId, Func<Guid, Task>? onMovieScannedAsync = null)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessMovieDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex, details.ExcludeFilters, onMovieScannedAsync);
    }

    public async Task ScanTvShowLibraryAsync(Guid libraryId, Func<Guid, Task>? onShowScannedAsync = null)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessTvDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex, details.ExcludeFilters, onShowScannedAsync);
    }

    public async Task ScanMusicLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessMusicDirectoriesAsync(library, details.FolderPaths, details.ExcludeFilters);
    }

    public async Task ScanMovieAsync(Guid movieId)
    {
        var item = new MediaItemHandle(movieId);
        var paths = await _ingestionService.GetMediaFilePathsAsync(item);
        if (!paths.Any()) return;

        var directories = paths
            .Select(p => Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .Cast<string>()
            .ToList();

        var library = await _ingestionService.GetLibraryForMediaAsync(item);
        if (library == null) return;

        var details = await _ingestionService.GetLibraryDetailsAsync(library.Value);
        await ProcessMovieDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters);
    }

    public async Task ScanTvShowAsync(Guid tvShowId)
    {
        var item = new MediaItemHandle(tvShowId);
        var paths = await _ingestionService.GetMediaFilePathsAsync(item);
        if (!paths.Any()) return;

        var directories = GetUniqueTvShowRootDirectories(paths);

        var library = await _ingestionService.GetLibraryForMediaAsync(item);
        if (library == null) return;

        var details = await _ingestionService.GetLibraryDetailsAsync(library.Value);
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters);
    }

    public async Task ScanSeasonAsync(Guid seasonId)
    {
        var item = new MediaItemHandle(seasonId);
        var paths = await _ingestionService.GetMediaFilePathsAsync(item);
        if (!paths.Any()) return;

        var directories = GetUniqueTvShowRootDirectories(paths);

        var library = await _ingestionService.GetLibraryForMediaAsync(item);
        if (library == null) return;

        var details = await _ingestionService.GetLibraryDetailsAsync(library.Value);
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters);
    }

    public async Task ScanEpisodeAsync(Guid episodeId)
    {
        var item = new MediaItemHandle(episodeId);
        var paths = await _ingestionService.GetMediaFilePathsAsync(item);
        if (!paths.Any()) return;

        var directories = GetUniqueTvShowRootDirectories(paths);

        var library = await _ingestionService.GetLibraryForMediaAsync(item);
        if (library == null) return;

        var details = await _ingestionService.GetLibraryDetailsAsync(library.Value);
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters);
    }

    // Radarr/Sonarr write an explicit edition token, e.g. "{edition-Director's Cut}".
    // Prefer that (it carries any edition text); fall back to the known-keyword list.
    private static readonly Regex EditionTagRegex = new(@"\{edition-(?<Edition>[^}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? ExtractEdition(string fileName, Regex editionKeywordRegex)
    {
        var tag = EditionTagRegex.Match(fileName);
        if (tag.Success) return tag.Groups["Edition"].Value.Trim();

        var keyword = editionKeywordRegex.Match(fileName);
        return keyword.Success ? keyword.Value.Trim() : null;
    }

    private (Regex titleRegex, Regex resolutionRegex, Regex editionRegex) BuildMovieRegexes(string? customRegex)
    {
        var regexPattern = customRegex ?? @"^(?<Title>.*?(?=\s*\(\d{4}\)|\s*\{|\s*\[|$))(?:\s*\((?<Year>\d{4})\))?(?:\s*\{(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^}]+)\})?";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        var resolutionRegex = new Regex(@"(?<Resolution>480p|720p|1080p|4k|2160p)", RegexOptions.IgnoreCase);
        var editionRegex = new Regex(@"(?i)\b(Extended|Director'?s\s*Cut|Unrated|Theatrical|Remastered|Ultimate|Final\s*Cut|Special\s*Edition|Collector'?s\s*Edition|Uncut|IMAX\s*Enhanced|IMAX|Alternate|Criterion|Anniversary|Black\s*Chrome|Coda|Definitive|Diamond|Platinum|Producer'?s\s*Cut|Richard\s*Donner|Ulysses|Open\s*Matte)\b", RegexOptions.IgnoreCase);
        return (regex, resolutionRegex, editionRegex);
    }

    private async Task ProcessMovieDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex, IReadOnlyList<string> excludeFilters, Func<Guid, Task>? onMovieScannedAsync = null)
    {
        var (regex, resolutionRegex, editionRegex) = BuildMovieRegexes(customRegex);

        await CleanupLegacyExtrasAsync(library);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var newFiles = GetNewFilesInDirectories(directories, existingPathsSet)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();
        var movieFiles = newFiles.Where(f => !IsExtraFile(f)).ToList();
        var extraFiles = newFiles.Where(IsExtraFile).ToList();

        var enriched = new HashSet<Guid>();
        for (int i = 0; i < movieFiles.Count; i++)
        {
            var filePath = movieFiles[i];
            _progress.Report($"Scanning {Path.GetFileNameWithoutExtension(filePath)} ({i + 1}/{movieFiles.Count})");
            var movieId = await IngestMovieFileAsync(library, filePath, regex, resolutionRegex, editionRegex);

            // Enrich each movie the first time it's ingested so its poster appears
            // mid-scan; multi-part movies resolve to the same id and only fire once.
            if (onMovieScannedAsync != null && movieId != Guid.Empty && enriched.Add(movieId))
            {
                await onMovieScannedAsync(movieId);
            }
        }

        foreach (var extraPath in extraFiles)
        {
            await IngestMovieExtraAsync(library, extraPath, regex);
        }
    }

    private async Task IngestMovieExtraAsync(LibraryHandle library, string extraPath, Regex titleRegex)
    {
        var rootFolder = GetMovieRootFolder(extraPath);
        var folderName = Path.GetFileName(rootFolder);
        if (string.IsNullOrEmpty(folderName)) return;

        var match = titleRegex.Match(folderName);
        string parentTitle = match.Success && match.Groups["Title"].Success ? match.Groups["Title"].Value.Trim() : folderName;
        int? parentYear = match.Groups["Year"].Success && int.TryParse(match.Groups["Year"].Value, out int year) ? year : null;

        var extraType = DetectExtraType(extraPath);
        var title = BuildExtraTitle(extraPath, extraType);

        await _ingestionService.AttachLocalExtraAsync(library, parentTitle, parentYear, extraPath, extraType, title);
    }

    private static string GetMovieRootFolder(string extraPath)
    {
        var dir = Path.GetDirectoryName(extraPath) ?? string.Empty;
        var name = Path.GetFileName(dir);
        if (!string.IsNullOrEmpty(name) && ExtraFolderNames.Contains(name))
        {
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return dir;
    }

    private static string DetectExtraType(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
        var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty).ToLowerInvariant();

        if (name.EndsWith("-trailer") || parent == "trailers") return "Trailer";
        if (name.EndsWith("-featurette") || name.EndsWith("-featurettes") || parent == "featurettes") return "Featurette";
        if (name.EndsWith("-behindthescenes") || parent == "behind the scenes" || parent == "behindthescenes") return "BehindTheScenes";
        if (name.EndsWith("-deleted") || name.EndsWith("-deletedscene") || name.EndsWith("-deletedscenes") || parent == "deleted scenes" || parent == "deletedscenes") return "DeletedScene";
        if (name.EndsWith("-interview") || name.EndsWith("-interviews") || parent == "interviews") return "Interview";
        if (name.EndsWith("-scene") || name.EndsWith("-scenes") || parent == "scenes") return "Scene";
        if (name.EndsWith("-short") || name.EndsWith("-shorts") || parent == "shorts") return "Short";
        if (name.EndsWith("-clip") || name.EndsWith("-clips") || parent == "clips") return "Clip";
        if (name == "sample" || name.EndsWith("-sample") || parent == "sample" || parent == "samples") return "Sample";
        return "Other";
    }

    private static string BuildExtraTitle(string extraPath, string extraType)
    {
        var fileName = Path.GetFileNameWithoutExtension(extraPath);
        var parentName = Path.GetFileName(Path.GetDirectoryName(extraPath) ?? string.Empty);

        if (!string.IsNullOrEmpty(parentName) && ExtraFolderNames.Contains(parentName))
        {
            return fileName;
        }

        var lower = fileName.ToLowerInvariant();
        var matched = ExtraFileSuffixes.FirstOrDefault(s => lower.EndsWith(s, StringComparison.Ordinal));
        if (matched != null)
        {
            var trimmed = fileName[..^matched.Length].TrimEnd(' ', '-', '.');
            return string.IsNullOrWhiteSpace(trimmed) ? extraType : trimmed;
        }

        return fileName;
    }

    private async Task<Guid> IngestMovieFileAsync(LibraryHandle library, string filePath, Regex regex, Regex resolutionRegex, Regex editionRegex)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var match = regex.Match(fileName);

        string title = fileName;
        int? year = null;
        string? provider = null;
        string? providerId = null;

        if (match.Success)
        {
            title = match.Groups["Title"].Success ? match.Groups["Title"].Value.Trim() : fileName;
            if (match.Groups["Year"].Success && int.TryParse(match.Groups["Year"].Value, out int parsedYear)) year = parsedYear;
            if (match.Groups["Provider"].Success) provider = match.Groups["Provider"].Value.ToLower();
            if (match.Groups["ProviderId"].Success) providerId = match.Groups["ProviderId"].Value;
        }

        var resMatch = resolutionRegex.Match(fileName);
        string? resolution = resMatch.Success ? resMatch.Groups["Resolution"].Value.ToLower() : null;

        string? edition = ExtractEdition(fileName, editionRegex);

        string? tmdbId = provider == "tmdb" ? providerId : null;
        string? imdbId = provider == "imdb" ? providerId : null;
        string? tvdbId = provider == "tvdb" ? providerId : null;

        var movieId = await _ingestionService.EnsureMovieAsync(library, title, year, tmdbId, imdbId, tvdbId, edition);
        await _ingestionService.AddMediaPartAsync(movieId, filePath, resolution, edition);
        return movieId.Value;
    }

    private (Regex episodeRegex, Regex showFolderRegex, Regex resolutionRegex, Regex editionRegex) BuildTvRegexes(string? customRegex)
    {
        var episodeRegexPattern = customRegex ?? @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";
        var episodeRegex = new Regex(episodeRegexPattern, RegexOptions.IgnoreCase);
        var showFolderRegex = new Regex(@"^(?<SeriesTitle>.+?)(?:\s*\((?<Year>\d{4})\))?(?:\s*\[(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^\]]+)\])?$", RegexOptions.IgnoreCase);
        var resolutionRegex = new Regex(@"(?<Resolution>480p|720p|1080p|4k|2160p)", RegexOptions.IgnoreCase);
        var editionRegex = new Regex(@"(?i)\b(Extended|Director'?s\s*Cut|Unrated|Theatrical|Remastered|Ultimate|Final\s*Cut|Special\s*Edition|Collector'?s\s*Edition|Uncut|IMAX\s*Enhanced|IMAX|Alternate|Criterion|Anniversary|Black\s*Chrome|Coda|Definitive|Diamond|Platinum|Producer'?s\s*Cut|Richard\s*Donner|Ulysses|Open\s*Matte)\b", RegexOptions.IgnoreCase);
        return (episodeRegex, showFolderRegex, resolutionRegex, editionRegex);
    }

    private async Task ProcessTvDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex, IReadOnlyList<string> excludeFilters, Func<Guid, Task>? onShowScannedAsync = null)
    {
        var (episodeRegex, showFolderRegex, resolutionRegex, editionRegex) = BuildTvRegexes(customRegex);

        await CleanupLegacyExtrasAsync(library);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var newFiles = GetNewFilesInDirectories(directories, existingPathsSet)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();
        var episodeFiles = newFiles.Where(f => !IsExtraFile(f)).ToList();
        var extraFiles = newFiles.Where(IsExtraFile).ToList();

        // Group by show root folder so we can enrich each show as soon as all of
        // its episodes are ingested — this makes posters appear per show during
        // the scan rather than after the whole library is scanned.
        var showGroups = episodeFiles.GroupBy(f => GetTvShowFolderName(f), StringComparer.OrdinalIgnoreCase);
        var scanned = 0;
        foreach (var group in showGroups)
        {
            Guid? showId = null;
            foreach (var filePath in group)
            {
                scanned++;
                _progress.Report($"Scanning {Path.GetFileNameWithoutExtension(filePath)} ({scanned}/{episodeFiles.Count})");
                var result = await IngestTvFileAsync(library, filePath, episodeRegex, showFolderRegex, resolutionRegex, editionRegex);
                if (result.ParentShowId.HasValue) showId = result.ParentShowId;
            }

            if (onShowScannedAsync != null && showId.HasValue)
            {
                await onShowScannedAsync(showId.Value);
            }
        }

        foreach (var extraPath in extraFiles)
        {
            await IngestTvExtraAsync(library, extraPath, showFolderRegex);
        }
    }

    private async Task IngestTvExtraAsync(LibraryHandle library, string extraPath, Regex showFolderRegex)
    {
        var showFolder = GetTvShowFolderName(extraPath);
        if (string.IsNullOrEmpty(showFolder)) return;

        var match = showFolderRegex.Match(showFolder);
        string showTitle = match.Success && match.Groups["SeriesTitle"].Success ? match.Groups["SeriesTitle"].Value.Trim() : showFolder;

        var extraType = DetectExtraType(extraPath);
        var title = BuildExtraTitle(extraPath, extraType);

        await _ingestionService.AttachTvShowLocalExtraAsync(library, showTitle, extraPath, extraType, title);
    }

    private static string GetTvShowFolderName(string extraPath)
    {
        var dir = Path.GetDirectoryName(extraPath);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name)
                && !ExtraFolderNames.Contains(name)
                && !name.StartsWith("Season", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Specials", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }

    private async Task<ScanFileResult> IngestTvFileAsync(LibraryHandle library, string filePath, Regex episodeRegex, Regex showFolderRegex, Regex resolutionRegex, Regex editionRegex)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);

        var seriesFolderName = directoryInfo.Name.StartsWith("Season", StringComparison.OrdinalIgnoreCase)
            ? directoryInfo.Parent?.Name ?? directoryInfo.Name
            : directoryInfo.Name;

        var showMatch = showFolderRegex.Match(seriesFolderName);
        string showTitle = showMatch.Success && showMatch.Groups["SeriesTitle"].Success ? showMatch.Groups["SeriesTitle"].Value.Trim() : seriesFolderName;
        int? showYear = showMatch.Groups["Year"].Success && int.TryParse(showMatch.Groups["Year"].Value, out int y) ? y : null;
        string? provider = showMatch.Groups["Provider"].Success ? showMatch.Groups["Provider"].Value.ToLower() : null;
        string? providerId = showMatch.Groups["ProviderId"].Success ? showMatch.Groups["ProviderId"].Value : null;

        var epMatch = episodeRegex.Match(fileName);
        int seasonNumber = 1, episodeNumber = 1;
        string? episodeTitle = null;
        DateTime? airDate = null;

        if (epMatch.Success)
        {
            if (epMatch.Groups["Season"].Success && int.TryParse(epMatch.Groups["Season"].Value, out int s)) seasonNumber = s;
            if (epMatch.Groups["Episode"].Success && int.TryParse(epMatch.Groups["Episode"].Value, out int e)) episodeNumber = e;

            if (epMatch.Groups["EpisodeTitle"].Success && !string.IsNullOrWhiteSpace(epMatch.Groups["EpisodeTitle"].Value))
            {
                episodeTitle = epMatch.Groups["EpisodeTitle"].Value.Trim(' ', '.', '-');
            }

            if (epMatch.Groups["AirDate"].Success && DateTime.TryParse(epMatch.Groups["AirDate"].Value, out DateTime parsedDate))
            {
                airDate = parsedDate;
            }
        }

        var resMatch = resolutionRegex.Match(fileName);
        string? resolution = resMatch.Success ? resMatch.Groups["Resolution"].Value.ToLower() : null;

        string? edition = ExtractEdition(fileName, editionRegex);

        string? tmdbId = provider == "tmdb" ? providerId : null;
        string? imdbId = provider == "imdb" ? providerId : null;
        string? tvdbId = provider == "tvdb" ? providerId : null;

        var showId = await _ingestionService.EnsureTvShowAsync(library, showTitle, showYear, tmdbId, imdbId, tvdbId);

        // Detect a genuinely new season BEFORE ensuring it, so the caller can
        // map the show's metadata exactly once for a new season (no per-file flood).
        var newSeason = !await _ingestionService.SeasonExistsAsync(showId, seasonNumber);
        var seasonId = await _ingestionService.EnsureSeasonAsync(library, showId, seasonNumber);

        string finalTitle = string.IsNullOrWhiteSpace(episodeTitle)
            ? $"{showTitle} - S{seasonNumber:D2}E{episodeNumber:D2}"
            : episodeTitle;

        var episodeId = await _ingestionService.EnsureEpisodeAsync(library, seasonId, episodeNumber, finalTitle, airDate, edition);

        await _ingestionService.AddMediaPartAsync(episodeId, filePath, resolution, edition);

        return new ScanFileResult(episodeId.Value, showId.Value, newSeason);
    }

    public async Task<Guid?> ScanMovieFileAsync(Guid libraryId, string filePath)
    {
        if (!_supportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return null;

        var library = LibraryHandle.FromGuid(libraryId);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        if (existing.Contains(filePath) || !File.Exists(filePath)) return null;

        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        if (IsExcluded(filePath, details.ExcludeFilters)) return null;

        var (regex, resolutionRegex, editionRegex) = BuildMovieRegexes(details.ScannerRegex);

        if (IsExtraFile(filePath))
        {
            await IngestMovieExtraAsync(library, filePath, regex);
            return null;
        }

        return await IngestMovieFileAsync(library, filePath, regex, resolutionRegex, editionRegex);
    }

    public async Task<ScanFileResult> ScanTvFileAsync(Guid libraryId, string filePath)
    {
        if (!_supportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return ScanFileResult.None;

        var library = LibraryHandle.FromGuid(libraryId);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        if (existing.Contains(filePath) || !File.Exists(filePath)) return ScanFileResult.None;

        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        if (IsExcluded(filePath, details.ExcludeFilters)) return ScanFileResult.None;

        var (episodeRegex, showFolderRegex, resolutionRegex, editionRegex) = BuildTvRegexes(details.ScannerRegex);

        if (IsExtraFile(filePath))
        {
            await IngestTvExtraAsync(library, filePath, showFolderRegex);
            return ScanFileResult.None;
        }

        return await IngestTvFileAsync(library, filePath, episodeRegex, showFolderRegex, resolutionRegex, editionRegex);
    }

    // Files ingested as standalone media items before extras existed (e.g. a
    // "Movie-trailer.mkv" that became a Movie) still linger as items and show up
    // in the library and search. Delete those items here; because the file is
    // then no longer "existing", the same scan re-ingests it as a proper extra
    // attached to its parent.
    private async Task CleanupLegacyExtrasAsync(LibraryHandle library)
    {
        var itemPaths = await _ingestionService.GetLibraryItemFilePathsAsync(library) ?? new List<string>();
        foreach (var path in itemPaths.Where(IsExtraFile))
        {
            await _ingestionService.RemoveMediaItemByPathAsync(path);
        }
    }

    // A file is skipped entirely if its name contains any of the library's
    // exclude substrings (e.g. ".TDARR" for files still being transcoded).
    private static bool IsExcluded(string filePath, IReadOnlyList<string> excludeFilters)
    {
        if (excludeFilters == null || excludeFilters.Count == 0) return false;
        var name = Path.GetFileName(filePath);
        foreach (var filter in excludeFilters)
        {
            if (!string.IsNullOrWhiteSpace(filter) && name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsExtraFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var lower = fileName.ToLowerInvariant();
            if (lower == "sample" || lower.EndsWith(".sample", StringComparison.Ordinal)) return true;
            foreach (var suffix in ExtraFileSuffixes)
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal)) return true;
            }
        }

        var parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        return !string.IsNullOrEmpty(parent) && ExtraFolderNames.Contains(parent);
    }

    private IEnumerable<string> GetNewFilesInDirectories(IEnumerable<string> directories, HashSet<string> existingPaths)
    {
        return GetNewFilesInDirectories(directories, existingPaths, _supportedExtensions);
    }

    private static IEnumerable<string> GetNewFilesInDirectories(IEnumerable<string> directories, HashSet<string> existingPaths, string[] extensions)
    {
        var newFiles = new List<string>();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                .Where(f => !existingPaths.Contains(f));

            newFiles.AddRange(files);
        }
        return newFiles;
    }

    private async Task ProcessMusicDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, IReadOnlyList<string> excludeFilters)
    {
        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var filesToProcess = GetNewFilesInDirectories(directories, existingPathsSet, _supportedAudioExtensions)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();

        if (filesToProcess.Count == 0) return;

        var parsed = new List<MusicFileMeta>();
        for (int i = 0; i < filesToProcess.Count; i++)
        {
            var filePath = filesToProcess[i];
            _progress.Report($"Scanning {Path.GetFileNameWithoutExtension(filePath)} ({i + 1}/{filesToProcess.Count})");
            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var tag = tagFile.Tag;

                var albumArtistTag = tag.FirstAlbumArtist;
                var trackArtistTag = tag.FirstPerformer;
                var artistName = FirstNonEmpty(albumArtistTag, trackArtistTag, "Unknown Artist");
                var albumTitle = string.IsNullOrWhiteSpace(tag.Album) ? "Unknown Album" : tag.Album;
                var trackTitle = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tag.Title;
                var isCompilationFlag = IsCompilationTag(tagFile, tag);

                byte[]? artworkBytes = null;
                string? artworkMime = null;
                if (tag.Pictures.Length > 0)
                {
                    var pic = tag.Pictures[0];
                    artworkBytes = pic.Data.Data;
                    artworkMime = pic.MimeType;
                }

                var contentRating = DetectAdvisory(tagFile, tag);

                parsed.Add(new MusicFileMeta
                {
                    FilePath = filePath,
                    ArtistName = artistName,
                    AlbumTitle = albumTitle,
                    TrackTitle = trackTitle,
                    TrackArtist = string.IsNullOrWhiteSpace(trackArtistTag) ? null : trackArtistTag,
                    AlbumArtistTag = string.IsNullOrWhiteSpace(albumArtistTag) ? null : albumArtistTag,
                    IsCompilationFlag = isCompilationFlag,
                    TrackNumber = (int)tag.Track,
                    DiscNumber = (int)tag.Disc,
                    Year = (int)tag.Year,
                    Genre = tag.FirstGenre,
                    DurationSeconds = (int?)tagFile.Properties?.Duration.TotalSeconds,
                    AudioCodec = tagFile.Properties?.Description,
                    SampleRate = tagFile.Properties?.AudioSampleRate,
                    Bitrate = tagFile.Properties?.AudioBitrate,
                    ArtworkBytes = artworkBytes,
                    ArtworkMimeType = artworkMime,
                    ContentRating = contentRating
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read tags for {FilePath}", filePath);
            }
        }

        var folderArtworkCache = new Dictionary<string, (byte[] Bytes, string Mime)?>(StringComparer.OrdinalIgnoreCase);

        var byArtist = parsed
            .GroupBy(p => p.ArtistName, StringComparer.OrdinalIgnoreCase);

        foreach (var artistGroup in byArtist)
        {
            var firstArtwork = artistGroup.FirstOrDefault(p => p.ArtworkBytes != null);
            byte[]? artistArtworkBytes = firstArtwork?.ArtworkBytes;
            string? artistArtworkMime = firstArtwork?.ArtworkMimeType;
            byte[]? artistBackgroundBytes = null;
            string? artistBackgroundMime = null;

            var artistFolders = artistGroup
                .Select(p => Path.GetDirectoryName(Path.GetDirectoryName(p.FilePath)))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (artistArtworkBytes == null)
            {
                foreach (var folder in artistFolders)
                {
                    var local = TryReadFolderArtwork(folder!, ArtistFolderArtworkNames, folderArtworkCache);
                    if (local != null)
                    {
                        artistArtworkBytes = local.Value.Bytes;
                        artistArtworkMime = local.Value.Mime;
                        break;
                    }
                }
            }

            foreach (var folder in artistFolders)
            {
                var local = TryReadFolderArtwork(folder!, FolderBackgroundNames, folderArtworkCache);
                if (local != null)
                {
                    artistBackgroundBytes = local.Value.Bytes;
                    artistBackgroundMime = local.Value.Mime;
                    break;
                }
            }

            byte[]? artistBannerBytes = null;
            string? artistBannerMime = null;
            foreach (var folder in artistFolders)
            {
                var local = TryReadFolderArtwork(folder!, ArtistBannerNames, folderArtworkCache);
                if (local != null)
                {
                    artistBannerBytes = local.Value.Bytes;
                    artistBannerMime = local.Value.Mime;
                    break;
                }
            }

            byte[]? artistLogoBytes = null;
            string? artistLogoMime = null;
            foreach (var folder in artistFolders)
            {
                var local = TryReadFolderArtwork(folder!, ArtistClearLogoNames, folderArtworkCache);
                if (local != null)
                {
                    artistLogoBytes = local.Value.Bytes;
                    artistLogoMime = local.Value.Mime;
                    break;
                }
            }

            var artistId = await _ingestionService.EnsureArtistAsync(
                library,
                artistGroup.Key,
                sortName: null,
                artworkBytes: artistArtworkBytes,
                artworkMimeType: artistArtworkMime,
                backgroundBytes: artistBackgroundBytes,
                backgroundMimeType: artistBackgroundMime,
                bannerBytes: artistBannerBytes,
                bannerMimeType: artistBannerMime,
                clearLogoBytes: artistLogoBytes,
                clearLogoMimeType: artistLogoMime);

            var byAlbum = artistGroup.GroupBy(p => p.AlbumTitle, StringComparer.OrdinalIgnoreCase);
            foreach (var albumGroup in byAlbum)
            {
                var sample = albumGroup.FirstOrDefault(p => p.ArtworkBytes != null);
                byte[]? albumArtworkBytes = sample?.ArtworkBytes;
                string? albumArtworkMime = sample?.ArtworkMimeType;
                byte[]? albumBackgroundBytes = null;
                string? albumBackgroundMime = null;

                var albumFolders = albumGroup
                    .Select(p => Path.GetDirectoryName(p.FilePath))
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (albumArtworkBytes == null)
                {
                    foreach (var folder in albumFolders)
                    {
                        var local = TryReadFolderArtwork(folder!, AlbumFolderArtworkNames, folderArtworkCache);
                        if (local != null)
                        {
                            albumArtworkBytes = local.Value.Bytes;
                            albumArtworkMime = local.Value.Mime;
                            break;
                        }
                    }
                }

                foreach (var folder in albumFolders)
                {
                    var local = TryReadFolderArtwork(folder!, FolderBackgroundNames, folderArtworkCache);
                    if (local != null)
                    {
                        albumBackgroundBytes = local.Value.Bytes;
                        albumBackgroundMime = local.Value.Mime;
                        break;
                    }
                }

                byte[]? albumDiscArtBytes = null;
                string? albumDiscArtMime = null;
                foreach (var folder in albumFolders)
                {
                    var local = TryReadFolderArtwork(folder!, AlbumDiscArtNames, folderArtworkCache);
                    if (local != null)
                    {
                        albumDiscArtBytes = local.Value.Bytes;
                        albumDiscArtMime = local.Value.Mime;
                        break;
                    }
                }

                var albumArtistDisplay = albumGroup
                    .Select(t => t.AlbumArtistTag)
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                    ?? artistGroup.Key;
                var compilationFromTag = albumGroup.Any(t => t.IsCompilationFlag);
                var distinctTrackArtists = albumGroup
                    .Select(t => t.TrackArtist)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var isCompilation = compilationFromTag || distinctTrackArtists > 1;

                var albumId = await _ingestionService.EnsureAlbumAsync(
                    library,
                    artistId,
                    albumGroup.Key,
                    year: albumGroup.Select(t => t.Year).FirstOrDefault(y => y > 0) is int yr && yr > 0 ? yr : null,
                    genre: albumGroup.Select(t => t.Genre).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g)),
                    artworkBytes: albumArtworkBytes,
                    artworkMimeType: albumArtworkMime,
                    backgroundBytes: albumBackgroundBytes,
                    backgroundMimeType: albumBackgroundMime,
                    discArtBytes: albumDiscArtBytes,
                    discArtMimeType: albumDiscArtMime,
                    albumArtist: albumArtistDisplay,
                    isCompilation: isCompilation);

                foreach (var track in albumGroup.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
                {
                    var trackId = await _ingestionService.EnsureTrackAsync(
                        library,
                        albumId,
                        track.TrackTitle,
                        track.TrackNumber,
                        track.DiscNumber > 0 ? track.DiscNumber : null,
                        track.DurationSeconds,
                        track.AudioCodec,
                        track.SampleRate,
                        track.Bitrate,
                        track.ContentRating,
                        trackArtist: track.TrackArtist);

                    await _ingestionService.AddMediaPartAsync(trackId, track.FilePath, resolution: null);
                }
            }
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static readonly string[] ArtistFolderArtworkNames =
    {
        "folder.jpg", "folder.jpeg", "folder.png",
        "poster.jpg", "poster.jpeg", "poster.png",
        "artist.jpg", "artist.jpeg", "artist.png",
        "thumb.jpg", "thumb.jpeg", "thumb.png"
    };

    private static readonly string[] AlbumFolderArtworkNames =
    {
        "folder.jpg", "folder.jpeg", "folder.png",
        "cover.jpg", "cover.jpeg", "cover.png",
        "front.jpg", "front.jpeg", "front.png",
        "album.jpg", "album.jpeg", "album.png"
    };

    private static readonly string[] FolderBackgroundNames =
    {
        "fanart.jpg", "fanart.jpeg", "fanart.png",
        "backdrop.jpg", "backdrop.jpeg", "backdrop.png",
        "background.jpg", "background.jpeg", "background.png"
    };

    private static readonly string[] ArtistBannerNames =
    {
        "banner.jpg", "banner.jpeg", "banner.png"
    };

    private static readonly string[] ArtistClearLogoNames =
    {
        "clearlogo.png", "clearlogo.jpg", "clearlogo.jpeg",
        "logo.png", "logo.jpg", "logo.jpeg"
    };

    private static readonly string[] AlbumDiscArtNames =
    {
        "discart.png", "discart.jpg", "discart.jpeg",
        "disc.png", "disc.jpg", "disc.jpeg",
        "cdart.png", "cdart.jpg", "cdart.jpeg"
    };

    private (byte[] Bytes, string Mime)? TryReadFolderArtwork(string folder, string[] candidateNames, Dictionary<string, (byte[] Bytes, string Mime)?> cache)
    {
        var cacheKey = folder + "::" + string.Join(",", candidateNames);
        if (cache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            foreach (var name in candidateNames)
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path)) continue;

                var bytes = File.ReadAllBytes(path);
                var mime = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => "image/jpeg"
                };
                var result = (bytes, mime);
                cache[cacheKey] = result;
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed reading folder artwork in {Folder}", folder);
        }

        cache[cacheKey] = null;
        return null;
    }

    private sealed class MusicFileMeta
    {
        public required string FilePath { get; init; }
        public required string ArtistName { get; init; }
        public required string AlbumTitle { get; init; }
        public required string TrackTitle { get; init; }
        public string? TrackArtist { get; init; }
        public string? AlbumArtistTag { get; init; }
        public bool IsCompilationFlag { get; init; }
        public int TrackNumber { get; init; }
        public int DiscNumber { get; init; }
        public int Year { get; init; }
        public string? Genre { get; init; }
        public int? DurationSeconds { get; init; }
        public string? AudioCodec { get; init; }
        public int? SampleRate { get; init; }
        public int? Bitrate { get; init; }
        public byte[]? ArtworkBytes { get; init; }
        public string? ArtworkMimeType { get; init; }
        public string? ContentRating { get; init; }
    }

    private static bool IsCompilationTag(TagLib.File tagFile, TagLib.Tag tag)
    {
        try
        {
            var appleTag = tagFile.GetTag(TagLib.TagTypes.Apple, false) as TagLib.Mpeg4.AppleTag;
            if (appleTag != null)
            {
                foreach (var box in appleTag)
                {
                    var fourcc = box.BoxType.ToString();
                    if (fourcc.Equals("cpil", StringComparison.OrdinalIgnoreCase))
                    {
                        if (box is TagLib.Mpeg4.AppleDataBox data && data.Data.Count > 0 && data.Data[0] != 0)
                            return true;
                    }
                }
            }

            var id3v2 = tagFile.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
            if (id3v2 != null)
            {
                foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.TextInformationFrame>())
                {
                    if (frame.FrameId.ToString() == "TCMP")
                    {
                        var value = frame.Text.FirstOrDefault();
                        if (!string.IsNullOrEmpty(value) && value != "0") return true;
                    }
                }
            }
        }
        catch
        {
        }

        var albumArtist = tag.FirstAlbumArtist;
        if (!string.IsNullOrWhiteSpace(albumArtist)
            && (albumArtist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase)
                || albumArtist.Equals("VA", StringComparison.OrdinalIgnoreCase)
                || albumArtist.Equals("Various", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string? DetectAdvisory(TagLib.File tagFile, TagLib.Tag tag)
    {
        try
        {
            var appleTag = tagFile.GetTag(TagLib.TagTypes.Apple, false) as TagLib.Mpeg4.AppleTag;
            if (appleTag != null)
            {
                foreach (var box in appleTag)
                {
                    var fourcc = box.BoxType.ToString();
                    if (fourcc.Contains("rtng", StringComparison.OrdinalIgnoreCase))
                    {
                        if (box is TagLib.Mpeg4.AppleDataBox data && data.Data.Count > 0)
                        {
                            var rating = data.Data[0];
                            if (rating == 1 || rating == 4) return "Explicit";
                            if (rating == 2) return "Clean";
                        }
                    }
                }
            }

            var id3v2 = tagFile.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
            if (id3v2 != null)
            {
                foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                {
                    if (string.Equals(frame.Description, "ITUNESADVISORY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(frame.Description, "PARENTAL_ADVISORY", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(frame.Description, "RATING WMP", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = frame.Text.FirstOrDefault();
                        if (string.IsNullOrEmpty(value)) continue;
                        if (value == "1" || value == "4" || value.Contains("Explicit", StringComparison.OrdinalIgnoreCase))
                            return "Explicit";
                        if (value == "2" || value.Contains("Clean", StringComparison.OrdinalIgnoreCase))
                            return "Clean";
                    }
                }
            }
        }
        catch
        {
            // best-effort tag read; ignore
        }

        if (!string.IsNullOrWhiteSpace(tag.Title) && tag.Title.Contains("[Explicit]", StringComparison.OrdinalIgnoreCase))
            return "Explicit";

        return null;
    }

    private IEnumerable<string> GetUniqueTvShowRootDirectories(IEnumerable<string> filePaths)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(path)!);

            if (dirInfo.Name.StartsWith("Season", StringComparison.OrdinalIgnoreCase) && dirInfo.Parent != null)
            {
                dirs.Add(dirInfo.Parent.FullName);
            }
            else
            {
                dirs.Add(dirInfo.FullName);
            }
        }
        return dirs;
    }
}
