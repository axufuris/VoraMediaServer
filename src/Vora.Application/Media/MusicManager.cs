using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Domain.Entities.Media;
using Vora.Application.Media.Requests;
using Vora.Application.Media.ViewModels;
using Vora.Application.Search.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IMusicManager
{
    Task<List<ArtistVM>> GetArtistsAsync(Guid? libraryId, MusicAccessFilter access, int? limit = null);
    Task<(ArtistVM? Artist, List<AlbumVM> Albums)> GetArtistDetailAsync(Guid artistId, Guid? profileId, MusicAccessFilter access);
    Task<(AlbumVM? Album, List<TrackVM> Tracks, string? ArtistBackgroundUrl)> GetAlbumDetailAsync(Guid albumId, Guid? profileId, MusicAccessFilter access);
    Task<List<ArtistTrackVM>> GetTracksForArtistAsync(Guid artistId, Guid? profileId, MusicAccessFilter access);
    Task<List<ArtistTrackVM>> GetTopTracksForArtistAsync(Guid artistId, Guid? profileId, MusicAccessFilter access, int limit);
    Task<string?> GetTrackFilePathAsync(Guid trackId, MusicAccessFilter access);

    Task<bool> UpdateArtistAsync(Guid artistId, UpdateArtistRequest request);
    Task<bool> UpdateAlbumAsync(Guid albumId, UpdateAlbumRequest request);
    Task<bool> UpdateTrackAsync(Guid trackId, UpdateTrackRequest request);

    Task<string?> SaveArtistArtworkAsync(Guid artistId, byte[] bytes, string? fileName);
    Task<string?> SaveAlbumArtworkAsync(Guid albumId, byte[] bytes, string? fileName);
    Task<string?> SaveArtistBackgroundAsync(Guid artistId, byte[] bytes, string? fileName);
    Task<string?> SaveAlbumBackgroundAsync(Guid albumId, byte[] bytes, string? fileName);
    Task<string?> SaveArtistBannerAsync(Guid artistId, byte[] bytes, string? fileName);
    Task<string?> SaveArtistClearLogoAsync(Guid artistId, byte[] bytes, string? fileName);
    Task<string?> SaveAlbumDiscArtAsync(Guid albumId, byte[] bytes, string? fileName);

    Task<List<MusicArtworkResult>> GetAlbumArtworkSuggestionsAsync(Guid albumId, CancellationToken cancellationToken);
    Task<List<MusicArtworkResult>> GetArtistArtworkSuggestionsAsync(Guid artistId, CancellationToken cancellationToken);

    Task<string?> RefreshArtistArtworkFromProvidersAsync(Guid artistId, bool force, CancellationToken cancellationToken);
    Task RefreshLibraryArtworkFromProvidersAsync(Guid libraryId, bool force, CancellationToken cancellationToken);
    Task<string?> RefreshAlbumArtworkFromProvidersAsync(Guid albumId, bool force, CancellationToken cancellationToken);

    Task<List<MusicSearchResultVM>> SearchAsync(string query, MusicAccessFilter access, int limit);

    Task<bool> SetTrackLikedAsync(Guid profileId, Guid trackId, bool liked);
    Task<List<ArtistTrackVM>> GetLikedTracksAsync(Guid profileId, MusicAccessFilter access);

    Task<SetMusicRatingResult> SetAlbumRatingAsync(Guid profileId, Guid albumId, decimal? rating, bool isAdmin);
    Task<SetMusicRatingResult> SetArtistRatingAsync(Guid profileId, Guid artistId, decimal? rating, bool isAdmin);

    Task<LyricsResult?> GetTrackLyricsAsync(Guid trackId, MusicAccessFilter access, CancellationToken cancellationToken);

    Task<RecordPlayOutcome> RecordTrackPlayAsync(Guid profileId, Guid trackId, int durationListenedSeconds, bool completed);
    Task UpdateNowPlayingAsync(Guid profileId, Guid trackId, CancellationToken cancellationToken);
    Task<List<ArtistTrackVM>> GetRecentlyPlayedAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<ArtistTrackVM>> GetTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<ArtistVM>> GetTopPlayedArtistsAsync(Guid profileId, MusicAccessFilter access, int limit);
    Task<List<AlbumVM>> GetRecentlyAddedAlbumsAsync(MusicAccessFilter access, int limit);
    Task<AlbumPageVM> GetAlbumsAsync(Guid? libraryId, MusicAccessFilter access, AlbumSortOrder sort, int offset, int limit, string? search = null);

    Task<List<GenreSummaryVM>> GetGenresAsync(MusicAccessFilter access);
    Task<GenreContentVM?> GetGenreContentAsync(string genre, MusicAccessFilter access);

    Task<AdminMusicHistoryVM> GetAdminMusicHistoryAsync(Guid? profileId, DateTime? from, DateTime? to, string? search, int page, int pageSize);
    Task<AdminMusicSummaryVM> GetAdminMusicSummaryAsync(DateTime? from, DateTime? to);

    Task<LastFmAuthStart?> StartLastFmAuthAsync(CancellationToken cancellationToken);
    Task<string?> CompleteLastFmAuthAsync(Guid profileId, string token, CancellationToken cancellationToken);
    Task DisconnectLastFmAsync(Guid profileId);
}

public sealed class LastFmAuthStart
{
    public required string Token { get; init; }
    public required string AuthUrl { get; init; }
}

public class MusicManager : IMusicManager
{
    public const int DefaultAlbumPageSize = 60;
    public const int MaxAlbumPageSize = 200;

    private const string MusicArtworkUrlPrefix = "/api/artwork/custom/";

    private readonly IMusicRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IUserMediaStateRepository _userMediaStateRepository;
    private readonly IEnumerable<IMusicArtworkProvider> _artworkProviders;
    private readonly IEnumerable<ILyricsProvider> _lyricsProviders;
    private readonly IEnumerable<IListeningDataProvider> _listeningProviders;
    private readonly IClientNotifier _notifier;
    private readonly Vora.Plugins.Interfaces.ITaskProgressReporter _progress;
    private readonly ILogger<MusicManager> _logger;
    private readonly string _artworkBasePath;

    public MusicManager(IMusicRepository repository, IUserRepository userRepository, IUserMediaStateRepository userMediaStateRepository, IEnumerable<IMusicArtworkProvider> artworkProviders, IEnumerable<ILyricsProvider> lyricsProviders, IEnumerable<IListeningDataProvider> listeningProviders, IClientNotifier notifier, IOptions<StoragePathsOptions> storagePaths, Vora.Plugins.Interfaces.ITaskProgressReporter progress, ILogger<MusicManager> logger)
    {
        _progress = progress;
        _repository = repository;
        _userRepository = userRepository;
        _userMediaStateRepository = userMediaStateRepository;
        _artworkProviders = artworkProviders;
        _lyricsProviders = lyricsProviders;
        _listeningProviders = listeningProviders;
        _notifier = notifier;
        _logger = logger;

        var configured = storagePaths.Value.CustomArtwork;
        _artworkBasePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        if (!Directory.Exists(_artworkBasePath))
        {
            Directory.CreateDirectory(_artworkBasePath);
        }
    }

    public async Task<List<ArtistVM>> GetArtistsAsync(Guid? libraryId, MusicAccessFilter access, int? limit = null)
    {
        var artists = await _repository.GetArtistsAsync(libraryId, access, limit);
        return artists.Select(MapArtist).ToList();
    }

    public async Task<(ArtistVM? Artist, List<AlbumVM> Albums)> GetArtistDetailAsync(Guid artistId, Guid? profileId, MusicAccessFilter access)
    {
        var artist = await _repository.GetArtistByIdAsync(artistId, access);
        if (artist == null) return (null, new List<AlbumVM>());

        var albums = await _repository.GetAlbumsForArtistAsync(artistId, access);
        var artistVm = MapArtist(artist);
        var albumVms = albums.Select(a => MapAlbum(a, artist.Name)).ToList();

        if (profileId.HasValue)
        {
            var artistRatings = await _repository.GetArtistRatingsAsync(profileId.Value, new[] { artistId });
            if (artistRatings.TryGetValue(artistId, out var ar)) artistVm.MyRating = ar;

            if (albumVms.Count > 0)
            {
                var albumRatings = await _repository.GetAlbumRatingsAsync(profileId.Value, albumVms.Select(a => a.Id));
                foreach (var av in albumVms)
                {
                    if (albumRatings.TryGetValue(av.Id, out var rating)) av.MyRating = rating;
                }
            }
        }

        return (artistVm, albumVms);
    }

    public async Task<SetMusicRatingResult> SetAlbumRatingAsync(Guid profileId, Guid albumId, decimal? rating, bool isAdmin)
    {
        if (rating.HasValue && (rating.Value < 0m || rating.Value > 10m))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 10.");
        }
        var result = await _repository.SetAlbumRatingAsync(profileId, albumId, rating, isAdmin);
        if (result.Found) await _notifier.NotifyMusicAlbumUpdatedAsync(albumId);
        return result;
    }

    public async Task<SetMusicRatingResult> SetArtistRatingAsync(Guid profileId, Guid artistId, decimal? rating, bool isAdmin)
    {
        if (rating.HasValue && (rating.Value < 0m || rating.Value > 10m))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 10.");
        }
        var result = await _repository.SetArtistRatingAsync(profileId, artistId, rating, isAdmin);
        if (result.Found) await _notifier.NotifyMusicArtistUpdatedAsync(artistId);
        return result;
    }

    public async Task<(AlbumVM? Album, List<TrackVM> Tracks, string? ArtistBackgroundUrl)> GetAlbumDetailAsync(Guid albumId, Guid? profileId, MusicAccessFilter access)
    {
        var album = await _repository.GetAlbumByIdAsync(albumId, access);
        if (album == null) return (null, new List<TrackVM>(), null);

        var tracks = await _repository.GetTracksForAlbumAsync(albumId, access);
        var likedIds = profileId.HasValue
            ? await _repository.GetLikedTrackIdsAsync(profileId.Value, tracks.Select(t => t.Id))
            : new HashSet<Guid>();

        var trackRatings = profileId.HasValue
            ? await _userMediaStateRepository.GetMediaRatingsAsync(profileId.Value, tracks.Select(t => t.Id))
            : new Dictionary<Guid, decimal>();

        var trackVms = tracks.Select(t =>
        {
            var vm = MapTrack(t);
            vm.IsLiked = likedIds.Contains(t.Id);
            if (trackRatings.TryGetValue(t.Id, out var r)) vm.MyRating = r;
            return vm;
        }).ToList();

        var albumVm = MapAlbum(album, album.Artist?.Name ?? string.Empty);
        if (profileId.HasValue)
        {
            var ratings = await _repository.GetAlbumRatingsAsync(profileId.Value, new[] { albumId });
            if (ratings.TryGetValue(albumId, out var rating)) albumVm.MyRating = rating;
        }

        // Only when the album has nothing of its own, so the common path costs
        // no extra query. Through the access filter, so a profile that cannot see
        // the artist gets null rather than a leaked url.
        string? artistBackgroundUrl = null;
        if (string.IsNullOrWhiteSpace(album.BackgroundUrl) && album.ArtistId != Guid.Empty)
        {
            var artist = await _repository.GetArtistByIdAsync(album.ArtistId, access);
            artistBackgroundUrl = artist?.BackgroundUrl;
        }

        return (albumVm, trackVms, artistBackgroundUrl);
    }

    public async Task<List<ArtistTrackVM>> GetTracksForArtistAsync(Guid artistId, Guid? profileId, MusicAccessFilter access)
    {
        var tracks = await _repository.GetTracksForArtistAsync(artistId, access);
        return await HydrateArtistTracksAsync(tracks, profileId);
    }

    private async Task<List<ArtistTrackVM>> HydrateArtistTracksAsync(List<Domain.Entities.Media.Track> tracks, Guid? profileId)
    {
        var likedIds = profileId.HasValue
            ? await _repository.GetLikedTrackIdsAsync(profileId.Value, tracks.Select(t => t.Id))
            : new HashSet<Guid>();
        var ratings = profileId.HasValue
            ? await _userMediaStateRepository.GetMediaRatingsAsync(profileId.Value, tracks.Select(t => t.Id))
            : new Dictionary<Guid, decimal>();

        return tracks.Select(t => new ArtistTrackVM
        {
            Id = t.Id,
            Title = t.Title,
            Artist = t.Artist,
            TrackNumber = t.TrackNumber,
            DiscNumber = t.DiscNumber,
            DurationSeconds = t.DurationSeconds,
            ContentRating = t.ContentRating,
            AlbumId = t.AlbumId,
            AlbumTitle = t.Album?.Title,
            AlbumArtworkUrl = t.Album?.ArtworkUrl,
            IsLiked = likedIds.Contains(t.Id),
            ServerAdminRating = t.ServerAdminRating,
            GlobalListeners = t.GlobalListeners,
            GlobalPlays = t.GlobalPlays,
            MyRating = ratings.TryGetValue(t.Id, out var r) ? r : (decimal?)null
        }).ToList();
    }

    public async Task<List<ArtistTrackVM>> GetTopTracksForArtistAsync(Guid artistId, Guid? profileId, MusicAccessFilter access, int limit)
    {
        var tracks = await _repository.GetTopTracksForArtistAsync(artistId, access, limit);
        return await HydrateArtistTracksAsync(tracks, profileId);
    }

    public Task<string?> GetTrackFilePathAsync(Guid trackId, MusicAccessFilter access) =>
        _repository.GetTrackFilePathAsync(trackId, access);

    public async Task<bool> UpdateArtistAsync(Guid artistId, UpdateArtistRequest request)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return false;

        if (!artist.IsLocked(nameof(artist.Name)) && !string.IsNullOrWhiteSpace(request.Name)) artist.Name = request.Name;
        if (!artist.IsLocked(nameof(artist.SortName))) artist.SortName = request.SortName;
        if (!artist.IsLocked(nameof(artist.Biography))) artist.Biography = request.Biography;
        if (!artist.IsLocked(nameof(artist.ArtworkUrl))) artist.ArtworkUrl = request.ArtworkUrl;
        if (!artist.IsLocked(nameof(artist.BackgroundUrl))) artist.BackgroundUrl = request.BackgroundUrl;
        if (!artist.IsLocked(nameof(artist.BannerUrl))) artist.BannerUrl = request.BannerUrl;
        if (!artist.IsLocked(nameof(artist.ClearLogoUrl))) artist.ClearLogoUrl = request.ClearLogoUrl;

        artist.LockedFields = request.LockedFields ?? new List<string>();
        await _repository.UpdateArtistAsync(artist);
        return true;
    }

    public async Task<bool> UpdateAlbumAsync(Guid albumId, UpdateAlbumRequest request)
    {
        var album = await _repository.GetAlbumForUpdateAsync(albumId);
        if (album == null) return false;

        if (!album.IsLocked(nameof(album.Title)) && !string.IsNullOrWhiteSpace(request.Title)) album.Title = request.Title;
        if (!album.IsLocked(nameof(album.SortTitle))) album.SortTitle = request.SortTitle;
        if (!album.IsLocked(nameof(album.Year))) album.Year = request.Year;
        if (!album.IsLocked(nameof(album.Genre))) album.Genre = request.Genre;
        if (!album.IsLocked(nameof(album.ArtworkUrl))) album.ArtworkUrl = request.ArtworkUrl;
        if (!album.IsLocked(nameof(album.BackgroundUrl))) album.BackgroundUrl = request.BackgroundUrl;
        if (!album.IsLocked(nameof(album.DiscArtUrl))) album.DiscArtUrl = request.DiscArtUrl;

        album.LockedFields = request.LockedFields ?? new List<string>();
        await _repository.UpdateAlbumAsync(album);
        return true;
    }

    public async Task<bool> UpdateTrackAsync(Guid trackId, UpdateTrackRequest request)
    {
        var track = await _repository.GetTrackForUpdateAsync(trackId);
        if (track == null) return false;

        if (!track.IsLocked(nameof(track.Title)) && !string.IsNullOrWhiteSpace(request.Title)) track.Title = request.Title;
        if (!track.IsLocked(nameof(track.SortTitle))) track.SortTitle = request.SortTitle;
        if (!track.IsLocked(nameof(track.TrackNumber))) track.TrackNumber = request.TrackNumber;
        if (!track.IsLocked(nameof(track.DiscNumber))) track.DiscNumber = request.DiscNumber;
        if (!track.IsLocked(nameof(track.ContentRating))) track.ContentRating = string.IsNullOrWhiteSpace(request.ContentRating) ? null : request.ContentRating.Trim();

        track.LockedFields = request.LockedFields ?? new List<string>();
        await _repository.UpdateTrackAsync(track);
        return true;
    }

    public async Task<string?> SaveArtistArtworkAsync(Guid artistId, byte[] bytes, string? fileName)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return null;

        var url = await SaveArtworkFileAsync($"artist_{artistId}", bytes, fileName);
        if (url == null) return null;

        artist.ArtworkUrl = url;
        artist.LockField(nameof(artist.ArtworkUrl));
        await _repository.UpdateArtistAsync(artist);
        return url;
    }

    public async Task<string?> SaveAlbumArtworkAsync(Guid albumId, byte[] bytes, string? fileName)
    {
        var album = await _repository.GetAlbumForUpdateAsync(albumId);
        if (album == null) return null;

        var url = await SaveArtworkFileAsync($"album_{albumId}", bytes, fileName);
        if (url == null) return null;

        album.ArtworkUrl = url;
        album.LockField(nameof(album.ArtworkUrl));
        await _repository.UpdateAlbumAsync(album);
        return url;
    }

    public async Task<string?> SaveArtistBackgroundAsync(Guid artistId, byte[] bytes, string? fileName)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return null;

        var url = await SaveArtworkFileAsync($"artist_bg_{artistId}", bytes, fileName);
        if (url == null) return null;

        artist.BackgroundUrl = url;
        artist.LockField(nameof(artist.BackgroundUrl));
        await _repository.UpdateArtistAsync(artist);
        return url;
    }

    public async Task<string?> SaveAlbumBackgroundAsync(Guid albumId, byte[] bytes, string? fileName)
    {
        var album = await _repository.GetAlbumForUpdateAsync(albumId);
        if (album == null) return null;

        var url = await SaveArtworkFileAsync($"album_bg_{albumId}", bytes, fileName);
        if (url == null) return null;

        album.BackgroundUrl = url;
        album.LockField(nameof(album.BackgroundUrl));
        await _repository.UpdateAlbumAsync(album);
        return url;
    }

    public async Task<string?> SaveArtistBannerAsync(Guid artistId, byte[] bytes, string? fileName)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return null;

        var url = await SaveArtworkFileAsync($"artist_banner_{artistId}", bytes, fileName);
        if (url == null) return null;

        artist.BannerUrl = url;
        artist.LockField(nameof(artist.BannerUrl));
        await _repository.UpdateArtistAsync(artist);
        return url;
    }

    public async Task<string?> SaveArtistClearLogoAsync(Guid artistId, byte[] bytes, string? fileName)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return null;

        var url = await SaveArtworkFileAsync($"artist_logo_{artistId}", bytes, fileName);
        if (url == null) return null;

        artist.ClearLogoUrl = url;
        artist.LockField(nameof(artist.ClearLogoUrl));
        await _repository.UpdateArtistAsync(artist);
        return url;
    }

    public async Task<string?> SaveAlbumDiscArtAsync(Guid albumId, byte[] bytes, string? fileName)
    {
        var album = await _repository.GetAlbumForUpdateAsync(albumId);
        if (album == null) return null;

        var url = await SaveArtworkFileAsync($"album_disc_{albumId}", bytes, fileName);
        if (url == null) return null;

        album.DiscArtUrl = url;
        album.LockField(nameof(album.DiscArtUrl));
        await _repository.UpdateAlbumAsync(album);
        return url;
    }

    public async Task<List<MusicArtworkResult>> GetAlbumArtworkSuggestionsAsync(Guid albumId, CancellationToken cancellationToken)
    {
        var album = await _repository.GetAlbumByIdAsync(albumId, MusicAccessFilter.Unrestricted);
        if (album == null) return new List<MusicArtworkResult>();

        var artistName = album.Artist?.Name ?? string.Empty;
        var albumTitle = album.Title;

        var tasks = _artworkProviders.Select(async provider =>
        {
            try
            {
                return await provider.SearchAlbumArtworkAsync(artistName, albumTitle, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Music artwork provider {Provider} failed for album {Album} by {Artist}", provider.ProviderName, albumTitle, artistName);
                return (IReadOnlyList<MusicArtworkResult>)Array.Empty<MusicArtworkResult>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return MergeAndDedupe(results);
    }

    public async Task<List<MusicArtworkResult>> GetArtistArtworkSuggestionsAsync(Guid artistId, CancellationToken cancellationToken)
    {
        var artist = await _repository.GetArtistByIdAsync(artistId, MusicAccessFilter.Unrestricted);
        if (artist == null) return new List<MusicArtworkResult>();

        var tasks = _artworkProviders.Select(async provider =>
        {
            try
            {
                return await provider.SearchArtistArtworkAsync(artist.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Music artwork provider {Provider} failed for artist {Artist}", provider.ProviderName, artist.Name);
                return (IReadOnlyList<MusicArtworkResult>)Array.Empty<MusicArtworkResult>();
            }
        });

        var results = await Task.WhenAll(tasks);
        return MergeAndDedupe(results);
    }

    // Unknown means the provider did not classify its results, so it only stands
    // in for the primary image — never for a background, banner or logo, where an
    // unclassified photo would be the wrong shape.
    private static string? PickArtwork(IReadOnlyList<MusicArtworkResult> suggestions, MusicArtworkKind kind)
    {
        var exact = suggestions.FirstOrDefault(s => s.Kind == kind && !string.IsNullOrWhiteSpace(s.Url));
        if (exact != null) return exact.Url;

        if (kind is MusicArtworkKind.Thumb or MusicArtworkKind.Cover)
        {
            return suggestions.FirstOrDefault(s => s.Kind == MusicArtworkKind.Unknown && !string.IsNullOrWhiteSpace(s.Url))?.Url;
        }

        return null;
    }

    // Artists and Albums are not MediaItems, so the library enrichment pass walks
    // straight past them — nothing was refreshing music artwork for a library that
    // already existed. The per-artist refresh only ever ran at the moment an
    // artist was created without embedded art, so an artist whose folder art had
    // filled ArtworkUrl never asked a provider for a background, banner or logo.
    public async Task RefreshLibraryArtworkFromProvidersAsync(Guid libraryId, bool force, CancellationToken cancellationToken)
    {
        var artistIds = await _repository.GetArtistIdsForArtworkRefreshAsync(libraryId, force);
        var albumIds = await _repository.GetAlbumIdsForArtworkRefreshAsync(libraryId, force);

        var total = artistIds.Count + albumIds.Count;
        if (total == 0) return;

        var done = 0;

        foreach (var artistId in artistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;
            _progress.Report($"Fetching music artwork ({done}/{total})");
            try
            {
                await RefreshArtistArtworkFromProvidersAsync(artistId, force, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Artwork refresh failed for artist {ArtistId}; continuing with the rest of the library.", artistId);
            }
        }

        foreach (var albumId in albumIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;
            _progress.Report($"Fetching music artwork ({done}/{total})");
            try
            {
                await RefreshAlbumArtworkFromProvidersAsync(albumId, force, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Artwork refresh failed for album {AlbumId}; continuing with the rest of the library.", albumId);
            }
        }

        _progress.Report(null);
        _logger.LogInformation("Music artwork refresh for library {LibraryId} covered {Total} item(s).", libraryId, total);
    }

    public async Task<string?> RefreshArtistArtworkFromProvidersAsync(Guid artistId, bool force, CancellationToken cancellationToken)
    {
        var artist = await _repository.GetArtistForUpdateAsync(artistId);
        if (artist == null) return null;

        // Each slot is locked and filled on its own. A single early return on the
        // artist image meant a locked photo also blocked the background, banner
        // and logo — and before results carried a Kind those three were never
        // filled at all: every image arrived in one list, the first was taken as
        // the artist photo, and the rest were discarded.
        var wanted = new (string Field, MusicArtworkKind Kind, Func<string?> Get, Action<string> Set)[]
        {
            (nameof(artist.ArtworkUrl), MusicArtworkKind.Thumb, () => artist.ArtworkUrl, v => artist.ArtworkUrl = v),
            (nameof(artist.BackgroundUrl), MusicArtworkKind.Background, () => artist.BackgroundUrl, v => artist.BackgroundUrl = v),
            (nameof(artist.BannerUrl), MusicArtworkKind.Banner, () => artist.BannerUrl, v => artist.BannerUrl = v),
            (nameof(artist.ClearLogoUrl), MusicArtworkKind.Logo, () => artist.ClearLogoUrl, v => artist.ClearLogoUrl = v),
        };

        var fillable = wanted
            .Where(slot => !artist.IsLocked(slot.Field))
            .Where(slot => force || string.IsNullOrEmpty(slot.Get()))
            .ToList();

        if (fillable.Count == 0) return artist.ArtworkUrl;

        var suggestions = await GetArtistArtworkSuggestionsAsync(artistId, cancellationToken);
        if (suggestions.Count == 0)
        {
            _logger.LogInformation("No artwork suggestions available for artist {Artist}", artist.Name);
            return null;
        }

        var applied = 0;
        foreach (var slot in fillable)
        {
            var pick = PickArtwork(suggestions, slot.Kind);
            if (string.IsNullOrWhiteSpace(pick)) continue;
            slot.Set(pick);
            applied++;
        }

        if (applied == 0) return null;

        await _repository.UpdateArtistAsync(artist);
        await _notifier.NotifyMusicArtistUpdatedAsync(artistId);
        _logger.LogInformation("Applied {Count} artwork image(s) for artist {Artist}", applied, artist.Name);
        return artist.ArtworkUrl;
    }

    public async Task<string?> RefreshAlbumArtworkFromProvidersAsync(Guid albumId, bool force, CancellationToken cancellationToken)
    {
        var album = await _repository.GetAlbumForUpdateAsync(albumId);
        if (album == null) return null;

        var wanted = new (string Field, MusicArtworkKind Kind, Func<string?> Get, Action<string> Set)[]
        {
            (nameof(album.ArtworkUrl), MusicArtworkKind.Cover, () => album.ArtworkUrl, v => album.ArtworkUrl = v),
            (nameof(album.BackgroundUrl), MusicArtworkKind.Background, () => album.BackgroundUrl, v => album.BackgroundUrl = v),
            (nameof(album.DiscArtUrl), MusicArtworkKind.Logo, () => album.DiscArtUrl, v => album.DiscArtUrl = v),
        };

        var fillable = wanted
            .Where(slot => !album.IsLocked(slot.Field))
            .Where(slot => force || string.IsNullOrEmpty(slot.Get()))
            .ToList();

        if (fillable.Count == 0) return album.ArtworkUrl;

        var suggestions = await GetAlbumArtworkSuggestionsAsync(albumId, cancellationToken);
        if (suggestions.Count == 0)
        {
            _logger.LogInformation("No artwork suggestions available for album {Album}", album.Title);
            return null;
        }

        var applied = 0;
        foreach (var slot in fillable)
        {
            var pick = PickArtwork(suggestions, slot.Kind);
            if (string.IsNullOrWhiteSpace(pick)) continue;
            slot.Set(pick);
            applied++;
        }

        if (applied == 0) return null;

        await _repository.UpdateAlbumAsync(album);
        await _notifier.NotifyMusicAlbumUpdatedAsync(albumId);
        _logger.LogInformation("Applied {Count} artwork image(s) for album {Album}", applied, album.Title);
        return album.ArtworkUrl;
    }

    public async Task<List<MusicSearchResultVM>> SearchAsync(string query, MusicAccessFilter access, int limit)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return new List<MusicSearchResultVM>();
        return await _repository.SearchAsync(query.Trim(), access, limit);
    }

    public Task<bool> SetTrackLikedAsync(Guid profileId, Guid trackId, bool liked) =>
        _repository.SetTrackLikedAsync(profileId, trackId, liked);

    public async Task<List<ArtistTrackVM>> GetLikedTracksAsync(Guid profileId, MusicAccessFilter access)
    {
        var tracks = await _repository.GetLikedTracksAsync(profileId, access);
        return tracks.Select(t => new ArtistTrackVM
        {
            Id = t.Id,
            Title = t.Title,
            Artist = t.Artist,
            TrackNumber = t.TrackNumber,
            DiscNumber = t.DiscNumber,
            DurationSeconds = t.DurationSeconds,
            ContentRating = t.ContentRating,
            AlbumId = t.AlbumId,
            AlbumTitle = t.Album?.Title,
            AlbumArtworkUrl = t.Album?.ArtworkUrl,
            IsLiked = true
        }).ToList();
    }

    public async Task<RecordPlayOutcome> RecordTrackPlayAsync(Guid profileId, Guid trackId, int durationListenedSeconds, bool completed)
    {
        // Looked up rather than taken from the scrobble info, which also comes
        // back null for a track that exists but carries no artist tag. Only a
        // genuinely missing track should stop the play being recorded.
        var track = await _repository.GetTrackByIdAsync(trackId, MusicAccessFilter.Unrestricted);
        if (track == null)
        {
            _logger.LogDebug("Not recording a play of track {Track}, which no longer exists.", trackId);
            return RecordPlayOutcome.UnknownTrack;
        }

        // The gate lives here rather than in the player, because the player is
        // only one of the clients that post plays. A web build, a TV client and a
        // phone each deciding for itself what counts would put several different
        // definitions of a play into one history table.
        if (!completed && !PlayQualification.Qualifies(durationListenedSeconds, track.DurationSeconds))
        {
            _logger.LogDebug(
                "Not recording a play of track {Track}: {Seconds}s of {Duration}s is below the threshold.",
                trackId, durationListenedSeconds, track.DurationSeconds);
            return RecordPlayOutcome.BelowThreshold;
        }

        await _repository.RecordPlayAsync(profileId, trackId, durationListenedSeconds, completed);

        // The play is stored either way. Scrobbling is a side effect that must
        // not change what the caller is told about its own request.
        await ScrobbleAsync(profileId, trackId);
        return RecordPlayOutcome.Recorded;
    }

    private async Task ScrobbleAsync(Guid profileId, Guid trackId)
    {
        if (!_listeningProviders.Any()) return;

        try
        {
            var profile = await _userRepository.GetProfileByIdAsync(profileId);
            if (profile == null || string.IsNullOrWhiteSpace(profile.LastFmSessionKey)) return;

            var info = await ResolveTrackScrobbleInfoAsync(trackId);
            if (info == null) return;

            foreach (var provider in _listeningProviders)
            {
                try
                {
                    await provider.ScrobbleAsync(profile.LastFmSessionKey, info.Value.Artist, info.Value.Track, info.Value.Album, DateTime.UtcNow, info.Value.Duration, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Scrobble via {Provider} failed for track {Track}", provider.ProviderName, trackId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scrobble pipeline failed for track {Track}", trackId);
        }
    }

    public async Task UpdateNowPlayingAsync(Guid profileId, Guid trackId, CancellationToken cancellationToken)
    {
        if (!_listeningProviders.Any()) return;
        try
        {
            var profile = await _userRepository.GetProfileByIdAsync(profileId);
            if (profile == null || string.IsNullOrWhiteSpace(profile.LastFmSessionKey)) return;

            var info = await ResolveTrackScrobbleInfoAsync(trackId);
            if (info == null) return;

            foreach (var provider in _listeningProviders)
            {
                try
                {
                    await provider.UpdateNowPlayingAsync(profile.LastFmSessionKey, info.Value.Artist, info.Value.Track, info.Value.Album, info.Value.Duration, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Now-playing via {Provider} failed for track {Track}", provider.ProviderName, trackId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Now-playing pipeline failed for track {Track}", trackId);
        }
    }

    private async Task<(string Artist, string Track, string? Album, int? Duration)?> ResolveTrackScrobbleInfoAsync(Guid trackId)
    {
        var track = await _repository.GetTrackByIdAsync(trackId, MusicAccessFilter.Unrestricted);
        if (track == null) return null;

        Domain.Entities.Media.Album? album = null;
        if (track.AlbumId.HasValue)
        {
            album = await _repository.GetAlbumByIdAsync(track.AlbumId.Value, MusicAccessFilter.Unrestricted);
        }

        var artistName = !string.IsNullOrWhiteSpace(track.Artist)
            ? track.Artist!
            : (album?.AlbumArtist ?? album?.Artist?.Name ?? string.Empty);

        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(track.Title)) return null;
        return (artistName, track.Title, album?.Title, track.DurationSeconds);
    }

    public async Task<LastFmAuthStart?> StartLastFmAuthAsync(CancellationToken cancellationToken)
    {
        var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
        if (provider == null) return null;
        var token = await provider.GetAuthTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return null;
        var url = await provider.BuildAuthUrlAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(url)) return null;
        return new LastFmAuthStart { Token = token, AuthUrl = url };
    }

    public async Task<string?> CompleteLastFmAuthAsync(Guid profileId, string token, CancellationToken cancellationToken)
    {
        var provider = _listeningProviders.FirstOrDefault(p => p.Id == "lastfm_listening");
        if (provider == null) return null;
        var session = await provider.ExchangeTokenForSessionAsync(token, cancellationToken);
        if (session == null) return null;

        var profile = await _userRepository.GetProfileByIdAsync(profileId);
        if (profile == null) return null;
        profile.LastFmSessionKey = session.SessionKey;
        profile.LastFmUsername = session.Username;
        await _userRepository.UpdateProfileAsync(profile);
        return session.Username;
    }

    public async Task DisconnectLastFmAsync(Guid profileId)
    {
        var profile = await _userRepository.GetProfileByIdAsync(profileId);
        if (profile == null) return;
        profile.LastFmSessionKey = null;
        profile.LastFmUsername = null;
        await _userRepository.UpdateProfileAsync(profile);
    }

    public async Task<List<ArtistTrackVM>> GetRecentlyPlayedAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var tracks = await _repository.GetRecentlyPlayedTracksAsync(profileId, access, limit);
        return await MapArtistTracksWithLikesAsync(profileId, tracks);
    }

    public async Task<List<ArtistTrackVM>> GetTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var tracks = await _repository.GetTopPlayedTracksAsync(profileId, access, limit);
        return await MapArtistTracksWithLikesAsync(profileId, tracks);
    }

    public async Task<List<ArtistVM>> GetTopPlayedArtistsAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var artists = await _repository.GetTopPlayedArtistsAsync(profileId, access, limit);
        return artists.Select(MapArtist).ToList();
    }

    public async Task<List<AlbumVM>> GetRecentlyAddedAlbumsAsync(MusicAccessFilter access, int limit)
    {
        var albums = await _repository.GetRecentlyAddedAlbumsAsync(access, limit);
        return albums.Select(a => MapAlbum(a, a.Artist?.Name ?? string.Empty)).ToList();
    }

    public async Task<AlbumPageVM> GetAlbumsAsync(Guid? libraryId, MusicAccessFilter access, AlbumSortOrder sort, int offset, int limit, string? search = null)
    {
        var pageOffset = Math.Max(0, offset);
        var pageLimit = Math.Clamp(limit, 1, MaxAlbumPageSize);

        // Normalised once, here, rather than at the endpoint and again in the
        // repository. A term of spaces is a term of nothing, and it has to reach
        // the query as absent so that "no search" is one state and not two.
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var (albums, total) = await _repository.GetAlbumsPageAsync(libraryId, access, sort, pageOffset, pageLimit, term);

        return new AlbumPageVM
        {
            Items = albums.Select(a => MapAlbum(a, a.Artist?.Name ?? string.Empty)).ToList(),
            Total = total,
            Offset = pageOffset,
            Limit = pageLimit
        };
    }

    public async Task<List<GenreSummaryVM>> GetGenresAsync(MusicAccessFilter access)
    {
        var summaries = await _repository.GetGenreSummariesAsync(access);
        return summaries.Select(s => new GenreSummaryVM
        {
            Name = s.Name,
            TrackCount = s.TrackCount,
            AlbumCount = s.AlbumCount,
            ArtistCount = s.ArtistCount,
            SampleArtworkUrl = s.SampleArtworkUrl
        }).ToList();
    }

    public async Task<GenreContentVM?> GetGenreContentAsync(string genre, MusicAccessFilter access)
    {
        if (string.IsNullOrWhiteSpace(genre)) return null;
        var content = await _repository.GetGenreContentAsync(genre, access);
        if (content.Artists.Count == 0 && content.Albums.Count == 0 && content.Tracks.Count == 0) return null;

        return new GenreContentVM
        {
            Name = content.Name,
            Artists = content.Artists.Select(MapArtist).ToList(),
            Albums = content.Albums.Select(a => MapAlbum(a, a.Artist?.Name ?? string.Empty)).ToList(),
            Tracks = content.Tracks.Select(MapTrack).ToList()
        };
    }

    public async Task<AdminMusicHistoryVM> GetAdminMusicHistoryAsync(Guid? profileId, DateTime? from, DateTime? to, string? search, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (rows, total) = await _repository.GetAdminPlayHistoryAsync(profileId, from, to, search, page, pageSize);
        return new AdminMusicHistoryVM
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Rows = rows.Select(r => new AdminMusicHistoryRowVM
            {
                Id = r.Id,
                ProfileId = r.ProfileId,
                ProfileName = r.ProfileName,
                TrackId = r.TrackId,
                TrackTitle = r.TrackTitle,
                Artist = r.Artist,
                AlbumTitle = r.AlbumTitle,
                AlbumArtworkUrl = r.AlbumArtworkUrl,
                PlayedAt = r.PlayedAt,
                DurationListenedSeconds = r.DurationListenedSeconds,
                Completed = r.Completed
            }).ToList()
        };
    }

    public async Task<AdminMusicSummaryVM> GetAdminMusicSummaryAsync(DateTime? from, DateTime? to)
    {
        var topTracks = await _repository.GetServerTopTracksAsync(from, to, 20);
        var topArtists = await _repository.GetServerTopArtistsAsync(from, to, 15);
        var perProfile = await _repository.GetPlaysPerProfileAsync(from, to);
        return new AdminMusicSummaryVM
        {
            TotalPlays = perProfile.Sum(p => p.PlayCount),
            DistinctProfileCount = perProfile.Count,
            TopTracks = topTracks.Select(t => new AdminTopTrackVM
            {
                TrackId = t.TrackId,
                TrackTitle = t.TrackTitle,
                Artist = t.Artist,
                AlbumTitle = t.AlbumTitle,
                AlbumArtworkUrl = t.AlbumArtworkUrl,
                PlayCount = t.PlayCount
            }).ToList(),
            TopArtists = topArtists.Select(a => new AdminTopArtistVM
            {
                ArtistId = a.ArtistId,
                ArtistName = a.ArtistName,
                ArtworkUrl = a.ArtworkUrl,
                PlayCount = a.PlayCount
            }).ToList(),
            PlaysPerProfile = perProfile.Select(p => new AdminProfilePlayCountVM
            {
                ProfileId = p.ProfileId,
                ProfileName = p.ProfileName,
                PlayCount = p.PlayCount
            }).ToList()
        };
    }

    private async Task<List<ArtistTrackVM>> MapArtistTracksWithLikesAsync(Guid profileId, List<Domain.Entities.Media.Track> tracks)
    {
        var likedIds = await _repository.GetLikedTrackIdsAsync(profileId, tracks.Select(t => t.Id));
        return tracks.Select(t => new ArtistTrackVM
        {
            Id = t.Id,
            Title = t.Title,
            Artist = t.Artist,
            TrackNumber = t.TrackNumber,
            DiscNumber = t.DiscNumber,
            DurationSeconds = t.DurationSeconds,
            ContentRating = t.ContentRating,
            AlbumId = t.AlbumId,
            AlbumTitle = t.Album?.Title,
            AlbumArtworkUrl = t.Album?.ArtworkUrl,
            IsLiked = likedIds.Contains(t.Id)
        }).ToList();
    }

    public async Task<LyricsResult?> GetTrackLyricsAsync(Guid trackId, MusicAccessFilter access, CancellationToken cancellationToken)
    {
        var track = await _repository.GetTrackByIdAsync(trackId, access);
        if (track == null) return null;

        Domain.Entities.Media.Album? album = null;
        if (track.AlbumId.HasValue)
        {
            album = await _repository.GetAlbumByIdAsync(track.AlbumId.Value, MusicAccessFilter.Unrestricted);
        }

        var artistName = album?.Artist?.Name ?? string.Empty;
        var albumTitle = album?.Title;
        var trackTitle = track.Title;
        var duration = track.DurationSeconds;

        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackTitle)) return null;

        var orderedProviders = _lyricsProviders
            .OrderBy(p => string.Equals(p.ProviderName, "LRClib", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);

        foreach (var provider in orderedProviders)
        {
            try
            {
                var result = await provider.GetLyricsAsync(artistName, trackTitle, albumTitle, duration, cancellationToken);
                if (result != null && result.HasAnyLyrics) return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lyrics provider {Provider} failed for {Artist} / {Track}", provider.ProviderName, artistName, trackTitle);
            }
        }

        return null;
    }

    private static List<MusicArtworkResult> MergeAndDedupe(IEnumerable<IReadOnlyList<MusicArtworkResult>> results)
    {
        var merged = new List<MusicArtworkResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerResults in results)
        {
            foreach (var item in providerResults)
            {
                if (string.IsNullOrWhiteSpace(item.Url)) continue;
                if (!seenUrls.Add(item.Url)) continue;
                merged.Add(item);
            }
        }
        return merged;
    }

    private async Task<string?> SaveArtworkFileAsync(string identityKey, byte[] bytes, string? sourceFileName)
    {
        try
        {
            var ext = Vora.Application.FileSystem.ImageContentValidator.DetectImageExtension(bytes);
            if (ext == null)
            {
                _logger.LogWarning("Rejected non-image artwork upload for {Key}", identityKey);
                return null;
            }
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{identityKey}_{DateTime.UtcNow.Ticks}"))).ToLowerInvariant()[..16];
            var fileName = $"music_upload_{hash}{ext}";
            var fullPath = Path.Combine(_artworkBasePath, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes);
            return $"{MusicArtworkUrlPrefix}{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save uploaded artwork for {Key}", identityKey);
            return null;
        }
    }

    private static ArtistVM MapArtist(Domain.Entities.Media.Artist a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        SortName = a.SortName,
        Biography = a.Biography,
        ArtworkUrl = a.ArtworkUrl,
        BackgroundUrl = a.BackgroundUrl,
        BannerUrl = a.BannerUrl,
        ClearLogoUrl = a.ClearLogoUrl,
        LibraryId = a.LibraryId,
        ServerAdminRating = a.ServerAdminRating,
        GlobalListeners = a.GlobalListeners,
        GlobalPlays = a.GlobalPlays,
        LockedFields = a.LockedFields ?? new List<string>()
    };

    private static AlbumVM MapAlbum(Domain.Entities.Media.Album a, string artistName) => new()
    {
        Id = a.Id,
        Title = a.Title,
        SortTitle = a.SortTitle,
        Year = a.Year,
        Genre = a.Genre,
        ArtworkUrl = a.ArtworkUrl,
        BackgroundUrl = a.BackgroundUrl,
        DiscArtUrl = a.DiscArtUrl,
        AlbumArtist = a.AlbumArtist,
        IsCompilation = a.IsCompilation,
        ArtistId = a.ArtistId,
        ArtistName = artistName,
        ServerAdminRating = a.ServerAdminRating,
        GlobalPlays = a.GlobalPlays,
        LockedFields = a.LockedFields ?? new List<string>()
    };

    private static TrackVM MapTrack(Domain.Entities.Media.Track t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        SortTitle = t.SortTitle,
        Artist = t.Artist,
        TrackNumber = t.TrackNumber,
        DiscNumber = t.DiscNumber,
        DurationSeconds = t.DurationSeconds,
        ContentRating = t.ContentRating,
        AlbumId = t.AlbumId,
        ServerAdminRating = t.ServerAdminRating,
        GlobalListeners = t.GlobalListeners,
        GlobalPlays = t.GlobalPlays,
        LockedFields = t.LockedFields ?? new List<string>()
    };
}
