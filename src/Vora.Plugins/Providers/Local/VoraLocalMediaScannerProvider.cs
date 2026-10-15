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

    private sealed record LocalScan(LocalMetadataMode Mode, LocalMediaFolders Folders, HashSet<string> KnownPaths, IReadOnlyList<string> LibraryRoots)
    {
        public bool ReadsDocuments => Mode != LocalMetadataMode.Off;
        public bool PrefersDocuments => Mode == LocalMetadataMode.Prefer;
        public Dictionary<string, MediaItemHandle?> ShowsMatchedByHand { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record TitleIdentity(string Title, int? Year, ExternalIdSet Ids, bool FromDocument);

    private async Task<LocalScan> BeginLocalScanAsync(LibraryHandle library, HashSet<string> knownPaths, IReadOnlyList<string> libraryRoots) =>
        new(await _ingestionService.GetLocalMetadataModeAsync(library), new LocalMediaFolders(_logger), knownPaths, libraryRoots);

    public async Task ScanMovieLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessMovieDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
    }

    public async Task ScanTvShowLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessTvDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
    }

    public async Task<List<ScanUnit>> DiscoverMovieScanUnitsAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var newFiles = GetNewFilesInDirectories(details.FolderPaths, existing)
            .Where(f => !IsExcluded(f, details.ExcludeFilters))
            .ToList();

        return newFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ScanUnit(Path.GetFileName(g.Key), g.ToList()))
            .ToList();
    }

    public async Task<List<ScanUnit>> DiscoverTvScanUnitsAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var (episodeNames, _, _, _) = BuildTvRegexes(details.ScannerRegex);
        await SeparateFilesStackedOnTheFirstEpisodeAsync(library, details.FolderPaths, existing, episodeNames, new LocalMediaFolders(_logger));
        await RepairEpisodeRangesAsync(library, details.FolderPaths, existing, episodeNames);
        var newFiles = GetNewFilesInDirectories(details.FolderPaths, existing)
            .Where(f => !IsExcluded(f, details.ExcludeFilters))
            .ToList();

        return newFiles
            .GroupBy(GetTvShowFolderName, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new ScanUnit(g.Key, g.ToList()))
            .ToList();
    }

    public async Task<Guid?> ScanMovieUnitAsync(Guid libraryId, IReadOnlyList<string> filePaths)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        var (regex, resolutionRegex, editionRegex) = BuildMovieRegexes(details.ScannerRegex);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var local = await BeginLocalScanAsync(library, existing, details.FolderPaths);

        Guid? movieId = null;
        foreach (var filePath in filePaths.Where(f => !existing.Contains(f)))
        {
            if (IsExtraFile(filePath))
            {
                await IngestMovieExtraAsync(library, filePath, regex, local);
                continue;
            }
            var id = await IngestMovieFileAsync(library, filePath, regex, resolutionRegex, editionRegex, local);
            if (id != Guid.Empty) movieId ??= id;
        }
        return movieId;
    }

    public async Task<Guid?> ScanTvUnitAsync(Guid libraryId, IReadOnlyList<string> filePaths)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        var (episodeNames, showFolderRegex, resolutionRegex, editionRegex) = BuildTvRegexes(details.ScannerRegex);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var local = await BeginLocalScanAsync(library, existing, details.FolderPaths);

        Guid? showId = null;
        var episodeFiles = filePaths.Where(f => !existing.Contains(f) && !IsExtraFile(f));
        foreach (var filePath in episodeFiles)
        {
            try
            {
                // Isolate per-file: a single file that fails to ingest must not
                // abort the rest of the show (which left the show created but with
                // no episodes — an orphan) and must be logged with its path so the
                // failure is diagnosable instead of silently swallowed upstream.
                var result = await IngestTvFileAsync(library, filePath, episodeNames, showFolderRegex, resolutionRegex, editionRegex, local);
                if (result.ParentShowId.HasValue) showId ??= result.ParentShowId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest TV episode file {FilePath}", filePath);
            }
        }

        foreach (var extraPath in filePaths.Where(f => !existing.Contains(f) && IsExtraFile(f)))
        {
            try
            {
                await IngestTvExtraAsync(library, extraPath, showFolderRegex, local);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest TV extra file {FilePath}", extraPath);
            }
        }
        return showId;
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
        await ProcessMovieDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex, details.ExcludeFilters, details.FolderPaths);
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

    private async Task ProcessMovieDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex, IReadOnlyList<string> excludeFilters, IReadOnlyList<string> libraryRoots)
    {
        var (regex, resolutionRegex, editionRegex) = BuildMovieRegexes(customRegex);

        await CleanupLegacyExtrasAsync(library);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var local = await BeginLocalScanAsync(library, existingPathsSet, libraryRoots);
        var newFiles = GetNewFilesInDirectories(directories, existingPathsSet)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();
        var movieFiles = newFiles.Where(f => !IsExtraFile(f)).ToList();
        var extraFiles = newFiles.Where(IsExtraFile).ToList();

        for (int i = 0; i < movieFiles.Count; i++)
        {
            var filePath = movieFiles[i];
            _progress.Report($"Scanning {Path.GetFileNameWithoutExtension(filePath)} ({i + 1}/{movieFiles.Count})");
            await IngestMovieFileAsync(library, filePath, regex, resolutionRegex, editionRegex, local);
        }

        foreach (var extraPath in extraFiles)
        {
            await IngestMovieExtraAsync(library, extraPath, regex, local);
        }
    }

    private async Task IngestMovieExtraAsync(LibraryHandle library, string extraPath, Regex titleRegex, LocalScan local)
    {
        var rootFolder = GetMovieRootFolder(extraPath);
        var folderName = Path.GetFileName(rootFolder);
        if (string.IsNullOrEmpty(folderName)) return;

        var match = titleRegex.Match(folderName);
        string parentTitle = match.Success && match.Groups["Title"].Success ? match.Groups["Title"].Value.Trim() : folderName;
        int? parentYear = match.Groups["Year"].Success && int.TryParse(match.Groups["Year"].Value, out int year) ? year : null;

        if (local.ReadsDocuments && FeatureIn(rootFolder, local) is { } feature)
        {
            var identity = ResolveMovieIdentity(feature, titleRegex, local);
            if (identity.FromDocument)
            {
                parentTitle = identity.Title;
                parentYear = identity.Year;
            }
        }

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

    private static string? FeatureIn(string folder, LocalScan local)
    {
        var files = local.Folders.Files(folder);
        var feature = files.FirstOrDefault(file => MediaFileExtensions.IsVideo(file) && !IsExtraFile(file));
        return feature != null && LocalMediaAssets.HoldsOneFilm(files, feature) ? feature : null;
    }

    private async Task<Guid> IngestMovieFileAsync(LibraryHandle library, string filePath, Regex regex, Regex resolutionRegex, Regex editionRegex, LocalScan local)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var resMatch = resolutionRegex.Match(fileName);
        string? resolution = resMatch.Success ? resMatch.Groups["Resolution"].Value.ToLower() : null;

        string? edition = ExtractEdition(fileName, editionRegex);

        var movieId = await FindMovieMatchedByHandAsync(library, filePath, local);
        if (movieId == null)
        {
            var identity = ResolveMovieIdentity(filePath, regex, local);
            movieId = await _ingestionService.EnsureMovieAsync(library, identity.Title, identity.Year, identity.Ids.TmdbId, identity.Ids.ImdbId, identity.Ids.TvdbId, edition);
        }

        await _ingestionService.AddMediaPartAsync(movieId.Value, filePath, resolution, edition);
        return movieId.Value.Value;
    }

    private async Task<MediaItemHandle?> FindMovieMatchedByHandAsync(LibraryHandle library, string filePath, LocalScan local)
    {
        if (local.KnownPaths.Count == 0) return null;

        var film = LocalMediaAssets.FilmKey(Path.GetFileNameWithoutExtension(filePath));
        var sibling = local.Folders.Files(Path.GetDirectoryName(filePath)).FirstOrDefault(file =>
            local.KnownPaths.Contains(file)
            && MediaFileExtensions.IsVideo(file)
            && !IsExtraFile(file)
            && LocalMediaAssets.FilmKey(Path.GetFileNameWithoutExtension(file)) == film);

        return sibling == null ? null : await _ingestionService.FindHandMatchedOwnerAsync(library, sibling);
    }

    private static TitleIdentity ResolveMovieIdentity(string filePath, Regex regex, LocalScan local)
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

        var tagged = TaggedIds(provider, providerId);
        var document = local.ReadsDocuments
            ? LocalIdentity.Trusted(LocalNfoReader.ReadMovie(filePath, local.Folders), year, tagged)
            : null;

        return document == null
            ? new TitleIdentity(title, year, tagged, false)
            : new TitleIdentity(document.Title ?? title, document.ReleaseDate?.Year ?? year, LocalIdentity.Merge(tagged, LocalIdentity.Ids(document)), true);
    }

    private static ExternalIdSet TaggedIds(string? provider, string? providerId) =>
        ExternalIdSet.From(
            provider == "tmdb" ? providerId : null,
            provider == "imdb" ? providerId : null,
            provider == "tvdb" ? providerId : null);

    private (EpisodeFileNames episodeNames, Regex showFolderRegex, Regex resolutionRegex, Regex editionRegex) BuildTvRegexes(string? customRegex)
    {
        var episodeNames = new EpisodeFileNames(customRegex);
        var showFolderRegex = new Regex(@"^(?<SeriesTitle>.+?)(?:\s*\((?<Year>\d{4})\))?(?:\s*\[(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^\]]+)\])?$", RegexOptions.IgnoreCase);
        var resolutionRegex = new Regex(@"(?<Resolution>480p|720p|1080p|4k|2160p)", RegexOptions.IgnoreCase);
        var editionRegex = new Regex(@"(?i)\b(Extended|Director'?s\s*Cut|Unrated|Theatrical|Remastered|Ultimate|Final\s*Cut|Special\s*Edition|Collector'?s\s*Edition|Uncut|IMAX\s*Enhanced|IMAX|Alternate|Criterion|Anniversary|Black\s*Chrome|Coda|Definitive|Diamond|Platinum|Producer'?s\s*Cut|Richard\s*Donner|Ulysses|Open\s*Matte)\b", RegexOptions.IgnoreCase);
        return (episodeNames, showFolderRegex, resolutionRegex, editionRegex);
    }

    private async Task ProcessTvDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex, IReadOnlyList<string> excludeFilters, IReadOnlyList<string> libraryRoots)
    {
        var (episodeNames, showFolderRegex, resolutionRegex, editionRegex) = BuildTvRegexes(customRegex);

        await CleanupLegacyExtrasAsync(library);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var local = await BeginLocalScanAsync(library, existingPathsSet, libraryRoots);
        var roots = directories.ToList();
        await SeparateFilesStackedOnTheFirstEpisodeAsync(library, roots, existingPathsSet, episodeNames, local.Folders);
        await RepairEpisodeRangesAsync(library, roots, existingPathsSet, episodeNames);

        var newFiles = GetNewFilesInDirectories(roots, existingPathsSet)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();
        var episodeFiles = newFiles.Where(f => !IsExtraFile(f)).ToList();
        var extraFiles = newFiles.Where(IsExtraFile).ToList();

        for (int i = 0; i < episodeFiles.Count; i++)
        {
            var filePath = episodeFiles[i];
            _progress.Report($"Scanning {Path.GetFileNameWithoutExtension(filePath)} ({i + 1}/{episodeFiles.Count})");
            await IngestTvFileAsync(library, filePath, episodeNames, showFolderRegex, resolutionRegex, editionRegex, local);
        }

        foreach (var extraPath in extraFiles)
        {
            await IngestTvExtraAsync(library, extraPath, showFolderRegex, local);
        }
    }

    private async Task SeparateFilesStackedOnTheFirstEpisodeAsync(LibraryHandle library, IReadOnlyList<string> roots, HashSet<string> knownFiles, EpisodeFileNames episodeNames, LocalMediaFolders folders)
    {
        var separated = new List<string>();
        foreach (var file in await _ingestionService.GetFirstEpisodeFilePathsAsync(library))
        {
            if (IsExtraFile(file) || !knownFiles.Contains(file) || !roots.Any(root => IsInside(file, root)) || !File.Exists(file)) continue;

            var placement = episodeNames.Place(Path.GetFileNameWithoutExtension(file), Path.GetFileName(Path.GetDirectoryName(file)),
                () => LocalNfoReader.ReadEpisodeNumbers(file, folders));
            if (placement is null or { Season: 1, Episode: 1 }) continue;

            await _ingestionService.RemoveMediaItemByPathAsync(file);
            knownFiles.Remove(file);
            separated.Add(file);
        }

        if (separated.Count > 0)
        {
            _logger.LogInformation("Took {Count} file(s) off episode 1 of season 1, where an older scan had put them because it could not read their names; they are added again as the episodes they are.", separated.Count);
        }
    }

    private async Task RepairEpisodeRangesAsync(LibraryHandle library, IReadOnlyList<string> roots, HashSet<string> knownFiles, EpisodeFileNames episodeNames)
    {
        var rangeByFile = new Dictionary<string, EpisodeRange>(StringComparer.Ordinal);
        foreach (var file in knownFiles)
        {
            if (IsExtraFile(file) || !roots.Any(root => IsInside(file, root))) continue;
            if (EpisodeRangeOf(Path.GetFileNameWithoutExtension(file), episodeNames) is { } range)
            {
                rangeByFile[file] = range;
            }
        }

        if (rangeByFile.Count > 0)
        {
            await _ingestionService.SetEpisodeRangesAsync(library, rangeByFile);
        }
    }

    internal static EpisodeRange? EpisodeRangeOf(string fileName, EpisodeFileNames episodeNames) =>
        episodeNames.Read(fileName) is { NumbersGuessed: false, Episode: int first, EndEpisode: int last } && last > first
            ? new EpisodeRange(first, last)
            : null;

    private static bool IsInside(string file, string root)
    {
        var trimmed = root.TrimEnd('/', '\\');
        return file.Length > trimmed.Length
            && file.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            && file[trimmed.Length] is '/' or '\\';
    }

    private async Task IngestTvExtraAsync(LibraryHandle library, string extraPath, Regex showFolderRegex, LocalScan local)
    {
        var showFolderPath = GetTvShowFolder(extraPath);
        var showFolder = Path.GetFileName(showFolderPath);
        if (string.IsNullOrEmpty(showFolder)) return;

        var showTitle = ResolveShowIdentity(showFolder, showFolderPath, showFolderRegex, local).Title;

        var extraType = DetectExtraType(extraPath);
        var title = BuildExtraTitle(extraPath, extraType);

        await _ingestionService.AttachTvShowLocalExtraAsync(library, showTitle, extraPath, extraType, title);
    }

    private static string GetTvShowFolderName(string extraPath) => Path.GetFileName(GetTvShowFolder(extraPath));

    private static string GetTvShowFolder(string extraPath)
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
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }

    private static TitleIdentity ResolveShowIdentity(string seriesFolderName, string? showDirectory, Regex showFolderRegex, LocalScan local)
    {
        var showMatch = showFolderRegex.Match(seriesFolderName);
        string showTitle = showMatch.Success && showMatch.Groups["SeriesTitle"].Success ? showMatch.Groups["SeriesTitle"].Value.Trim() : seriesFolderName;
        int? showYear = showMatch.Groups["Year"].Success && int.TryParse(showMatch.Groups["Year"].Value, out int y) ? y : null;
        string? provider = showMatch.Groups["Provider"].Success ? showMatch.Groups["Provider"].Value.ToLower() : null;
        string? providerId = showMatch.Groups["ProviderId"].Success ? showMatch.Groups["ProviderId"].Value : null;

        var tagged = TaggedIds(provider, providerId);
        var document = local.ReadsDocuments && !string.IsNullOrEmpty(showDirectory)
            ? LocalIdentity.Trusted(LocalNfoReader.ReadTvShow(showDirectory, local.Folders), showYear, tagged)
            : null;

        return document == null
            ? new TitleIdentity(showTitle, showYear, tagged, false)
            : new TitleIdentity(document.Title ?? showTitle, document.ReleaseDate?.Year ?? showYear, LocalIdentity.Merge(tagged, LocalIdentity.Ids(document)), true);
    }

    private async Task<MediaItemHandle?> FindShowMatchedByHandAsync(LibraryHandle library, string filePath, string? showDirectory, LocalScan local)
    {
        if (string.IsNullOrEmpty(showDirectory) || local.KnownPaths.Count == 0) return null;
        if (local.LibraryRoots.Any(root => SameFolder(root, showDirectory))) return null;
        if (local.ShowsMatchedByHand.TryGetValue(showDirectory, out var known)) return known;

        var sibling = local.Folders.Files(Path.GetDirectoryName(filePath)).FirstOrDefault(file => IsKnownEpisode(file, local))
            ?? local.KnownPaths.FirstOrDefault(path => IsInside(path, showDirectory) && !IsExtraFile(path));

        var owner = sibling == null ? null : await _ingestionService.FindHandMatchedOwnerAsync(library, sibling);
        local.ShowsMatchedByHand[showDirectory] = owner;
        return owner;
    }

    private static bool IsKnownEpisode(string file, LocalScan local) =>
        local.KnownPaths.Contains(file) && MediaFileExtensions.IsVideo(file) && !IsExtraFile(file);

    private static bool SameFolder(string left, string right) =>
        left.TrimEnd('/', '\\').Equals(right.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private async Task<ScanFileResult> IngestTvFileAsync(LibraryHandle library, string filePath, EpisodeFileNames episodeNames, Regex showFolderRegex, Regex resolutionRegex, Regex editionRegex, LocalScan local)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(filePath)!);

        var placement = episodeNames.Place(fileName, directoryInfo.Name, () => LocalNfoReader.ReadEpisodeNumbers(filePath, local.Folders));
        if (placement == null)
        {
            _logger.LogWarning("Left {FilePath} out of the library: its name has no season and episode Vora can read, and no .nfo beside it gives them.", filePath);
            return ScanFileResult.None;
        }

        // A "Specials" (Season 0) subfolder is a season, not the show — walk up to
        // the show folder just like a "Season NN" folder. Missing this made every
        // show's Specials episodes group under a phantom show called "Specials".
        var seriesFolderName = directoryInfo.Name.StartsWith("Season", StringComparison.OrdinalIgnoreCase)
            || directoryInfo.Name.StartsWith("Specials", StringComparison.OrdinalIgnoreCase)
            ? directoryInfo.Parent?.Name ?? directoryInfo.Name
            : directoryInfo.Name;

        var showDirectory = local.ReadsDocuments || local.KnownPaths.Count > 0
            ? LocalMediaAssets.ShowDirectory(filePath, local.Folders)
            : null;
        var showMatchedByHand = await FindShowMatchedByHandAsync(library, filePath, showDirectory, local);
        var show = ResolveShowIdentity(seriesFolderName, showMatchedByHand == null ? showDirectory : null, showFolderRegex, local);
        var showTitle = show.Title;

        int seasonNumber = placement.Season, episodeNumber = placement.Episode;
        int? endEpisodeNumber = placement.EndEpisode;
        string? episodeTitle = placement.Title;
        DateOnly? airDate = placement.AirDate;

        var resMatch = resolutionRegex.Match(fileName);
        string? resolution = resMatch.Success ? resMatch.Groups["Resolution"].Value.ToLower() : null;

        string? edition = ExtractEdition(fileName, editionRegex);

        if (local.PrefersDocuments)
        {
            var localEpisode = LocalNfoReader.ReadEpisode(filePath, seasonNumber, episodeNumber, local.Folders);
            episodeTitle = localEpisode?.Title ?? episodeTitle;
            airDate = localEpisode?.ReleaseDate ?? airDate;
        }

        var showId = showMatchedByHand
            ?? await _ingestionService.EnsureTvShowAsync(library, show.Title, show.Year, show.Ids.TmdbId, show.Ids.ImdbId, show.Ids.TvdbId);

        // Detect a genuinely new season BEFORE ensuring it, so the caller can
        // map the show's metadata exactly once for a new season (no per-file flood).
        var newSeason = !await _ingestionService.SeasonExistsAsync(showId, seasonNumber);
        var seasonId = await _ingestionService.EnsureSeasonAsync(library, showId, seasonNumber);

        string finalTitle = string.IsNullOrWhiteSpace(episodeTitle)
            ? $"{showTitle} - S{seasonNumber:D2}E{episodeNumber:D2}"
            : episodeTitle;

        var episodeId = await _ingestionService.EnsureEpisodeAsync(library, seasonId, episodeNumber, finalTitle, airDate, edition, endEpisodeNumber);

        await _ingestionService.AddMediaPartAsync(episodeId, filePath, resolution, edition);

        return new ScanFileResult(episodeId.Value, showId.Value, newSeason);
    }

    public async Task<Guid?> ScanMovieFileAsync(Guid libraryId, string filePath)
    {
        if (!MediaFileExtensions.IsVideo(filePath)) return null;

        var library = LibraryHandle.FromGuid(libraryId);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        if (existing.Contains(filePath) || !File.Exists(filePath)) return null;

        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        if (IsExcluded(filePath, details.ExcludeFilters)) return null;

        var (regex, resolutionRegex, editionRegex) = BuildMovieRegexes(details.ScannerRegex);

        var local = await BeginLocalScanAsync(library, existing, details.FolderPaths);

        if (IsExtraFile(filePath))
        {
            await IngestMovieExtraAsync(library, filePath, regex, local);
            return null;
        }

        return await IngestMovieFileAsync(library, filePath, regex, resolutionRegex, editionRegex, local);
    }

    public async Task<ScanFileResult> ScanTvFileAsync(Guid libraryId, string filePath)
    {
        if (!MediaFileExtensions.IsVideo(filePath)) return ScanFileResult.None;

        var library = LibraryHandle.FromGuid(libraryId);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        if (existing.Contains(filePath) || !File.Exists(filePath)) return ScanFileResult.None;

        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        if (IsExcluded(filePath, details.ExcludeFilters)) return ScanFileResult.None;

        var (episodeNames, showFolderRegex, resolutionRegex, editionRegex) = BuildTvRegexes(details.ScannerRegex);

        var local = await BeginLocalScanAsync(library, existing, details.FolderPaths);

        if (IsExtraFile(filePath))
        {
            await IngestTvExtraAsync(library, filePath, showFolderRegex, local);
            return ScanFileResult.None;
        }

        return await IngestTvFileAsync(library, filePath, episodeNames, showFolderRegex, resolutionRegex, editionRegex, local);
    }

    public async Task<Guid?> ScanMusicFileAsync(Guid libraryId, string filePath)
    {
        if (!MediaFileExtensions.IsAudio(filePath)) return null;

        var library = LibraryHandle.FromGuid(libraryId);
        var existing = await _ingestionService.GetExistingLibraryPathsAsync(library);
        if (existing.Contains(filePath) || !File.Exists(filePath)) return null;

        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        if (IsExcluded(filePath, details.ExcludeFilters)) return null;

        return await IngestMusicFilesAsync(library, new[] { filePath });
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

    private static bool IsExcluded(string filePath, IReadOnlyList<string> excludeFilters) =>
        LibraryFileFilter.IsExcluded(filePath, excludeFilters);

    internal static bool IsExtraFile(string filePath)
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
        return GetNewFilesInDirectories(directories, existingPaths, MediaFileExtensions.Video);
    }

    private IEnumerable<string> GetNewFilesInDirectories(IEnumerable<string> directories, HashSet<string> existingPaths, IReadOnlyList<string> extensions)
    {
        var newFiles = new List<string>();
        foreach (var dir in directories.Where(Directory.Exists))
        {
            foreach (var file in EnumerateFilesResiliently(dir))
            {
                if (!extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                if (existingPaths.Contains(file)) continue;
                newFiles.Add(file);
            }
        }
        return newFiles;
    }

    private IEnumerable<string> EnumerateFilesResiliently(string root) =>
        ResilientDirectory.EnumerateFiles(
            root,
            (directory, ex) => _logger.LogWarning(ex, "Could not read {Directory}; skipping it and continuing the scan.", directory));

    private async Task ProcessMusicDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, IReadOnlyList<string> excludeFilters)
    {
        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var filesToProcess = GetNewFilesInDirectories(directories, existingPathsSet, MediaFileExtensions.Audio)
            .Where(f => !IsExcluded(f, excludeFilters))
            .ToList();

        await IngestMusicFilesAsync(library, filesToProcess);
    }

    private async Task<Guid?> IngestMusicFilesAsync(LibraryHandle library, IReadOnlyList<string> filesToProcess)
    {
        if (filesToProcess.Count == 0) return null;

        Guid? lastTrackId = null;
        var failures = 0;

        // The parse loop below reports per file, then the write phase reported
        // nothing at all — so a large library sat on "(n/n)" for as long as the
        // writes took and looked hung. The two phases are separately slow; both
        // have to be visible.
        var written = 0;
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
                    AudioCodec = ResolveAudioCodec(filePath, tagFile.Properties?.Description),
                    SampleRate = tagFile.Properties?.AudioSampleRate,
                    Bitrate = tagFile.Properties?.AudioBitrate,
                    ArtworkBytes = artworkBytes,
                    ArtworkMimeType = artworkMime,
                    ContentRating = contentRating,
                    Isrc = tag.ISRC
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
            try
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
                        written++;
                        _progress.Report($"Saving {artistGroup.Key} - {albumGroup.Key} ({written}/{filesToProcess.Count})");

                        // One unwritable track must not cost the library every track
                        // queued behind it. This loop used to be unguarded, so a
                        // single row the database rejected aborted the whole scan
                        // task and left the library holding whatever happened to be
                        // written first — an arbitrary subset, because the file order
                        // is the filesystem's, not alphabetical.
                        try
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
                                trackArtist: track.TrackArtist,
                                isrc: track.Isrc);

                            await _ingestionService.AddMediaPartAsync(trackId, track.FilePath, resolution: null, syncEdition: false);
                            lastTrackId = trackId.Value;
                        }
                        catch (Exception ex)
                        {
                            failures++;
                            _logger.LogError(ex, "Failed to ingest track {FilePath}; continuing with the rest of the library.", track.FilePath);
                        }
                    }

                    // Every save re-runs change detection over everything tracked
                    // so far, so a library-sized ingest on one DbContext gets
                    // slower with each album. Nothing here holds an entity across
                    // albums — only ids — so releasing them keeps that cost flat.
                    await _ingestionService.ReleaseTrackedEntitiesAsync();
                }
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogError(ex, "Failed to ingest artist {Artist}; continuing with the rest of the library.", artistGroup.Key);
            }
        }

        // Surfaced as a single line so a partial ingest is visible in the admin
        // Logs page without reading every per-track error. A silent skip would
        // repeat the original failure mode in a quieter form.
        if (failures > 0)
        {
            _logger.LogWarning("Music scan finished with {Failures} of {Total} file(s) not ingested.", failures, filesToProcess.Count);
        }

        return lastTrackId;
    }

    // TagLib's Properties.Description is prose ("MPEG Version 1 Audio, Layer 3
    // VBR"), and Track.AudioCodec is varchar(32). The VBR form is 33 characters,
    // so storing it raw threw 22001 and — because the ingest loop had no
    // isolation — killed the whole library scan at the first VBR mp3.
    //
    // Length was only half of it. Every consumer substring-matches a short codec
    // token (BadgeResolver picks mp3.png/flac.png; MediaDedupeManager scores
    // lossless vs lossy), and the prose form matches none of them, so even the
    // tracks that did fit got no audio badge and the wrong dedupe score. The
    // container is the reliable signal; the description only disambiguates the
    // two codecs that share the .m4a container.
    internal static string? ResolveAudioCodec(string filePath, string? description)
    {
        var lossless = description != null
            && (description.Contains("lossless", StringComparison.OrdinalIgnoreCase)
                || description.Contains("alac", StringComparison.OrdinalIgnoreCase));

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "mp3",
            ".flac" => "flac",
            ".m4a" => lossless ? "alac" : "aac",
            ".aac" => "aac",
            ".ogg" => "vorbis",
            ".opus" => "opus",
            ".wav" => "pcm",
            ".wma" => "wma",
            _ => Truncate(description, 32)
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value != null && value.Length > maxLength ? value[..maxLength] : value;

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
        public string? Isrc { get; init; }
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

    private static readonly string[] AdvisoryFields = { "ITUNESADVISORY", "PARENTAL_ADVISORY", "EXPLICIT" };

    // iTunes codes: 1 and 4 explicit, 2 clean, 0 none. A 0 is left unrated: it
    // is also what a tagger writes when nobody said anything, so it is not a
    // statement that the song is clean.
    internal static string? ReadAdvisoryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value == "1" || value == "4"
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Explicit", StringComparison.OrdinalIgnoreCase))
            return "Explicit";
        if (value == "2" || value.Contains("Clean", StringComparison.OrdinalIgnoreCase))
            return "Clean";
        return null;
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
                    // Not "RATING WMP": that is Windows Media Player's STAR rating,
                    // stored as 1 / 25 / 50 / 75 / 99. Reading it as an advisory
                    // turned a one-star song into "Explicit" — value "1" is the
                    // iTunes code for explicit — and hid it from every restricted
                    // profile for a reason that had nothing to do with its content.
                    if (AdvisoryFields.Contains(frame.Description, StringComparer.OrdinalIgnoreCase))
                    {
                        var advisory = ReadAdvisoryValue(frame.Text.FirstOrDefault());
                        if (advisory != null) return advisory;
                    }
                }
            }

            // FLAC and Ogg carry Vorbis comments, where downloaders and taggers
            // write the same advisory under the same names as the ID3 frames.
            var xiph = tagFile.GetTag(TagLib.TagTypes.Xiph, false) as TagLib.Ogg.XiphComment;
            if (xiph != null)
            {
                foreach (var field in AdvisoryFields)
                {
                    var advisory = ReadAdvisoryValue(xiph.GetFirstField(field));
                    if (advisory != null) return advisory;
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

            if ((dirInfo.Name.StartsWith("Season", StringComparison.OrdinalIgnoreCase)
                 || dirInfo.Name.StartsWith("Specials", StringComparison.OrdinalIgnoreCase)) && dirInfo.Parent != null)
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
