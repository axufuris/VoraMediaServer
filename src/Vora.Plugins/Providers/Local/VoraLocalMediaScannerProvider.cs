using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class VoraLocalMediaScannerProvider : ILocalMediaScannerProvider
{
    private readonly ILogger<VoraLocalMediaScannerProvider> _logger;
    private readonly IMediaIngestionService _ingestionService;
    private readonly string[] _supportedExtensions = { ".mkv", ".mp4", ".avi", ".m4v" };
    private readonly string[] _supportedAudioExtensions = { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aac", ".wma" };

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
        IMediaIngestionService ingestionService)
    {
        _logger = logger;
        _ingestionService = ingestionService;
    }

    public async Task ScanMovieLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessMovieDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex);
    }

    public async Task ScanTvShowLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessTvDirectoriesAsync(library, details.FolderPaths, details.ScannerRegex);
    }

    public async Task ScanMusicLibraryAsync(Guid libraryId)
    {
        var library = LibraryHandle.FromGuid(libraryId);
        var details = await _ingestionService.GetLibraryDetailsAsync(library);
        await ProcessMusicDirectoriesAsync(library, details.FolderPaths);
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
        await ProcessMovieDirectoriesAsync(library.Value, directories, details.ScannerRegex);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex);
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
        await ProcessTvDirectoriesAsync(library.Value, directories, details.ScannerRegex);
    }

    private async Task ProcessMovieDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex)
    {
        var regexPattern = customRegex ?? @"^(?<Title>.*?(?=\s*\(\d{4}\)|\s*\{|\s*\[|$))(?:\s*\((?<Year>\d{4})\))?(?:\s*\{(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^}]+)\})?";
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        var resolutionRegex = new Regex(@"(?<Resolution>480p|720p|1080p|4k|2160p)", RegexOptions.IgnoreCase);

        var editionRegex = new Regex(@"(?i)\b(Extended|Director'?s\s*Cut|Unrated|Theatrical|Remastered|Ultimate|Final\s*Cut|Special\s*Edition|Collector'?s\s*Edition|Uncut|IMAX\s*Enhanced|IMAX|Alternate|Criterion|Anniversary|Black\s*Chrome|Coda|Definitive|Diamond|Platinum|Producer'?s\s*Cut|Richard\s*Donner|Ulysses|Open\s*Matte)\b", RegexOptions.IgnoreCase);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var filesToProcess = GetNewFilesInDirectories(directories, existingPathsSet);

        foreach (var filePath in filesToProcess)
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

            var edMatch = editionRegex.Match(fileName);
            string? edition = edMatch.Success ? edMatch.Value.Trim() : null;

            string? tmdbId = provider == "tmdb" ? providerId : null;
            string? imdbId = provider == "imdb" ? providerId : null;
            string? tvdbId = provider == "tvdb" ? providerId : null;

            var movieId = await _ingestionService.EnsureMovieAsync(library, title, year, tmdbId, imdbId, tvdbId, edition);
            await _ingestionService.AddMediaPartAsync(movieId, filePath, resolution);
        }
    }

    private async Task ProcessTvDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories, string? customRegex)
    {
        var episodeRegexPattern = customRegex ?? @"(?:[sS](?<Season>\d{1,4})[eE](?<Episode>\d{1,4})(?:\s*-\s*(?<Absolute>\d{1,4}))?|(?<AirDate>\d{4}-\d{2}-\d{2}))\s*-\s*(?<EpisodeTitle>.*?)(?:\s*\[.*)?$";
        var episodeRegex = new Regex(episodeRegexPattern, RegexOptions.IgnoreCase);
        var showFolderRegex = new Regex(@"^(?<SeriesTitle>.+?)(?:\s*\((?<Year>\d{4})\))?(?:\s*\[(?<Provider>imdb|tmdb|tvdb)-(?<ProviderId>[^\]]+)\])?$", RegexOptions.IgnoreCase);
        var resolutionRegex = new Regex(@"(?<Resolution>480p|720p|1080p|4k|2160p)", RegexOptions.IgnoreCase);

        var editionRegex = new Regex(@"(?i)\b(Extended|Director'?s\s*Cut|Unrated|Theatrical|Remastered|Ultimate|Final\s*Cut|Special\s*Edition|Collector'?s\s*Edition|Uncut|IMAX\s*Enhanced|IMAX|Alternate|Criterion|Anniversary|Black\s*Chrome|Coda|Definitive|Diamond|Platinum|Producer'?s\s*Cut|Richard\s*Donner|Ulysses|Open\s*Matte)\b", RegexOptions.IgnoreCase);

        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var filesToProcess = GetNewFilesInDirectories(directories, existingPathsSet);

        foreach (var filePath in filesToProcess)
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

            var edMatch = editionRegex.Match(fileName);
            string? edition = edMatch.Success ? edMatch.Value.Trim() : null;

            string? tmdbId = provider == "tmdb" ? providerId : null;
            string? imdbId = provider == "imdb" ? providerId : null;
            string? tvdbId = provider == "tvdb" ? providerId : null;

            var showId = await _ingestionService.EnsureTvShowAsync(library, showTitle, showYear, tmdbId, imdbId, tvdbId);
            var seasonId = await _ingestionService.EnsureSeasonAsync(library, showId, seasonNumber);

            string finalTitle = string.IsNullOrWhiteSpace(episodeTitle)
                ? $"{showTitle} - S{seasonNumber:D2}E{episodeNumber:D2}"
                : episodeTitle;

            var episodeId = await _ingestionService.EnsureEpisodeAsync(library, seasonId, episodeNumber, finalTitle, airDate, edition);

            await _ingestionService.AddMediaPartAsync(episodeId, filePath, resolution);
        }
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

    private async Task ProcessMusicDirectoriesAsync(LibraryHandle library, IEnumerable<string> directories)
    {
        var existingPathsSet = await _ingestionService.GetExistingLibraryPathsAsync(library);
        var filesToProcess = GetNewFilesInDirectories(directories, existingPathsSet, _supportedAudioExtensions).ToList();

        if (filesToProcess.Count == 0) return;

        var parsed = new List<MusicFileMeta>();
        foreach (var filePath in filesToProcess)
        {
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
