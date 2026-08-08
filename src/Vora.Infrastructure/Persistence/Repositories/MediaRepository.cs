using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence.Extensions;

namespace Vora.Infrastructure.Persistence.Repositories;

public partial class MediaRepository : IMediaRepository
{
    private readonly ILogger<MediaRepository> _logger;
    private readonly VoraDbContext _context;

    public MediaRepository(ILogger<MediaRepository> logger, VoraDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<List<Guid>> GetEpisodeIdsForSeasonAsync(Guid seasonId) =>
        await _context.Set<Episode>().AsNoTracking().Where(e => e.SeasonId == seasonId).Select(e => e.Id).ToListAsync();

    public async Task<List<Guid>> GetEpisodeIdsForShowAsync(Guid tvShowId) =>
        await _context.Set<Episode>().AsNoTracking().Where(e => e.Season != null && e.Season.TvShowId == tvShowId).Select(e => e.Id).ToListAsync();

    public Task<List<Guid>> GetMediaIdsMissingTvdbIdAsync() =>
        _context.MediaItems
            .AsNoTracking()
            .Where(m => (m is Movie || m is TvShow) && m.TvdbId == null)
            .Select(m => m.Id)
            .ToListAsync();

    public async Task<Dictionary<Guid, string>> GetDisplayTitlesByIdsAsync(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        var rows = await _context.MediaItems
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.Title,
                Kind = m is Episode ? "episode" : m is Season ? "season" : "other",
                SeasonNumber = m is Episode ? ((Episode)m).Season.SeasonNumber
                    : m is Season ? ((Season)m).SeasonNumber
                    : (int?)null,
                EpisodeNumber = m is Episode ? ((Episode)m).EpisodeNumber : (int?)null,
                ShowTitle = m is Episode ? ((Episode)m).Season.TvShow.Title
                    : m is Season ? ((Season)m).TvShow.Title
                    : null
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.Id, r => r.Kind switch
        {
            "episode" => $"{r.ShowTitle} — S{r.SeasonNumber:D2}E{r.EpisodeNumber:D2} — {r.Title}",
            "season" => $"{r.ShowTitle} — {r.Title}",
            _ => r.Title
        });
    }

    public async Task<Guid?> GetMovieIdByTitleAndYearAsync(string title, int? year, Guid libraryId)
    {
        // Match on a normalized title (lowercased, punctuation stripped) so a
        // freshly-parsed filename title still matches an existing item whose
        // Title was rewritten by metadata — e.g. "Avatar The Way of Water" vs
        // "Avatar: The Way of Water". Exact/case-sensitive matching here would
        // create a duplicate item instead of merging the new file as a part.
        var normalized = NormalizeTitle(title);
        var candidates = await _context.Set<Movie>()
            .AsNoTracking()
            .Where(m => m.LibraryId == libraryId && (year == null || (m.ReleaseDate != null && m.ReleaseDate.Value.Year == year)))
            .Select(m => new { m.Id, m.Title })
            .ToListAsync();

        return candidates.FirstOrDefault(c => NormalizeTitle(c.Title) == normalized)?.Id;
    }

    public Task<Guid?> GetMovieIdByExternalIdAsync(string? tmdbId, string? imdbId, Guid libraryId)
    {
        if (string.IsNullOrWhiteSpace(tmdbId) && string.IsNullOrWhiteSpace(imdbId))
        {
            return Task.FromResult<Guid?>(null);
        }

        return _context.Set<Movie>()
            .AsNoTracking()
            .Where(m => m.LibraryId == libraryId
                && ((tmdbId != null && m.TmdbId == tmdbId) || (imdbId != null && m.ImdbId == imdbId)))
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync();
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var ch in title)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    public async Task<Guid?> GetTvShowIdByTitleAsync(string title, Guid libraryId)
    {
        return await _context.Set<TvShow>()
            .Where(t => t.Title == title && t.LibraryId == libraryId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid?> GetSeasonIdByNumberAsync(Guid tvShowId, int seasonNumber)
    {
        return await _context.Set<Season>()
            .Where(s => s.TvShowId == tvShowId && s.SeasonNumber == seasonNumber)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid?> GetEpisodeIdByNumberAsync(Guid seasonId, int episodeNumber)
    {
        return await _context.Set<Episode>()
            .Where(e => e.SeasonId == seasonId && e.EpisodeNumber == episodeNumber)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<HashSet<string>> GetExistingLibraryPathsAsync(Guid libraryId)
    {
        var existingPaths = await _context.MediaParts
            .AsNoTracking()
            .Where(p => p.MediaItem != null && p.MediaItem.LibraryId == libraryId)
            .Select(p => p.FilePath)
            .ToListAsync();

        var extraPaths = await _context.MediaParts
            .AsNoTracking()
            .Where(p => p.MediaExtraId != null && p.MediaExtra!.MediaItem.LibraryId == libraryId)
            .Select(p => p.FilePath)
            .ToListAsync();

        var set = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        set.UnionWith(extraPaths);
        return set;
    }

    public async Task<List<string>> GetLibraryItemFilePathsAsync(Guid libraryId)
    {
        return await _context.MediaParts
            .AsNoTracking()
            .Where(p => p.MediaItem != null && p.MediaItem.LibraryId == libraryId)
            .Select(p => p.FilePath)
            .ToListAsync();
    }

    public Task<Vora.Application.Media.Dtos.MediaMatchInfoDto?> GetMediaMatchInfoAsync(Guid mediaItemId) =>
        _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaItemId && (m is Movie || m is TvShow))
            .Select(m => new Vora.Application.Media.Dtos.MediaMatchInfoDto
            {
                TmdbId = m.TmdbId,
                ImdbId = m.ImdbId,
                Title = m.Title,
                Year = m.ReleaseDate != null ? m.ReleaseDate.Value.Year : (int?)null,
                MediaType = m is Movie ? "Movie" : "TvShow"
            })
            .FirstOrDefaultAsync();

    public async Task<List<Guid>> GetMediaIdsByExternalIdsAsync(List<string> tmdbIds, List<string> imdbIds)
    {
        return await _context.MediaItems
            .AsNoTracking()
            .Where(m =>
                (m.TmdbId != null && tmdbIds.Contains(m.TmdbId)) ||
                (m.ImdbId != null && imdbIds.Contains(m.ImdbId)))
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<Guid>> GetAllMediaItemIdsByLibraryAsync(Guid libraryId)
    {
        return await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.LibraryId == libraryId)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<string>> GetMediaFilePathsAsync(Guid mediaItemId)
    {
        return await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaItemId)
            .SelectMany(m => m.MediaParts.Select(p => p.FilePath))
            .ToListAsync();
    }

    public async Task<T?> GetProjectedAsync<T>(Guid id, Expression<Func<MediaItem, T>> projection, bool hasAllAccess = true, List<Guid>? allowedLibs = null, bool hasAllRatings = true, List<string>? allowedMovieRatings = null, List<string>? allowedTvRatings = null, bool blockUnrated = false)
    {
        var query = _context.MediaItems.AsNoTracking().AsSplitQuery().Where(m => m.Id == id);
        query = query.ApplyAccessFilters(hasAllAccess, allowedLibs ?? new List<Guid>(), hasAllRatings, allowedMovieRatings ?? new List<string>(), allowedTvRatings ?? new List<string>(), blockUnrated);

        if (!hasAllAccess && allowedLibs != null) query = query.Where(m => allowedLibs.Contains(m.LibraryId));

        return await query.Select(projection).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<T>> GetAllProjectedAsync<T>(Expression<Func<MediaItem, T>> projection, Guid? libraryId = null, string? libraryType = null, bool hasAllAccess = true, List<Guid>? allowedLibs = null, bool hasAllRatings = true, List<string>? allowedMovieRatings = null, List<string>? allowedTvRatings = null, bool blockUnrated = false)
    {
        var query = _context.MediaItems.AsNoTracking().AsSplitQuery();
        query = query.ApplyAccessFilters(hasAllAccess, allowedLibs ?? new List<Guid>(), hasAllRatings, allowedMovieRatings ?? new List<string>(), allowedTvRatings ?? new List<string>(), blockUnrated);

        if (!hasAllAccess && allowedLibs != null) query = query.Where(m => allowedLibs.Contains(m.LibraryId));

        if (libraryId.HasValue) query = query.Where(m => m.LibraryId == libraryId.Value && !(m is Episode) && !(m is Season));

        if (!string.IsNullOrEmpty(libraryType))
        {
            if (libraryType == "Movie") query = query.OfType<Movie>();
            else if (libraryType == "TvShow") query = query.OfType<TvShow>();
            else if (libraryType == "Season") query = query.OfType<Season>();
            else if (libraryType == "Episode") query = query.OfType<Episode>();
        }

        return await query.OrderBy(m => m is TvShow ? 0 : m is Movie ? 0 : m is Season ? 1 : 2).Select(projection).ToListAsync();
    }

    public async Task<MediaItem?> GetForAnalysisAsync(Guid id)
    {
        return await _context.MediaItems
            .Include(m => m.Analysis)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.AudioTracks)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.VideoTracks)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.SubtitleTracks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MediaItem?> GetForPosterOverlayAsync(Guid id)
    {
        return await _context.MediaItems
            .AsNoTracking()
            .Include(m => m.Analysis)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.AudioTracks)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.VideoTracks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MediaItem?> GetForMetadataSyncAsync(Guid id)
    {
        return await _context.MediaItems
            .Include(m => m.Analysis)
            .Include(m => m.MediaParts)
            .Include(m => m.Library)
            .Include(m => m.Genres)
            .Include(m => m.Cast).ThenInclude(c => c.Actor)
            .Include(m => m.Collections)
            .Include(m => m.ProductionCompanies)
            .Include(m => m.OriginCountries)
            .Include(m => m.Videos)
            .Include(m => ((TvShow)m).Networks)
            .Include(m => ((TvShow)m).Seasons)
            .Include(m => ((Season)m).TvShow)
            .Include(m => ((Episode)m).Season).ThenInclude(s => s.TvShow).ThenInclude(t => t.Cast).ThenInclude(c => c.Actor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IEnumerable<Guid>> GetMediaIdsMissingMetadataAsync(Guid libraryId)
    {
        return await _context.MediaItems
            .Where(m => m.LibraryId == libraryId &&
                (
                    m.LastMetadataRefresh == null
                    ||
                    ((m is Movie || m is TvShow) &&
                     (m.TmdbId == null ||
                      m.PosterUrl == null ||
                      m.BackgroundUrl == null ||
                      m.Overview == null ||
                      m.ReleaseDate == null ||
                      m.ContentRating == null ||
                      !m.Genres.Any() ||
                      !m.Cast.Any()))
                    ||
                    (m is Episode &&
                     (m.TmdbId == null ||
                      m.Overview == null ||
                      m.PosterUrl == null ||
                      m.BackgroundUrl == null ||
                      m.ReleaseDate == null))
                    ||
                    (m is Season && m.PosterUrl == null)
                ))
            .OrderBy(m => m is TvShow ? 0 : m is Movie ? 0 : m is Season ? 1 : 2)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<Guid>> GetMediaIdsMissingArtworkAsync(Guid libraryId)
    {
        return await _context.MediaItems
            .Where(m => m.LibraryId == libraryId && m.PosterUrl == null)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<Guid>> GetMediaIdsMissingRatingsAsync(Guid libraryId)
    {
        return await _context.MediaItems
            .Where(m => m.LibraryId == libraryId
                && (m is Movie || m is TvShow)
                && m.ThirdPartyRating1 == null)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<MediaItem?> GetForBasicUpdateAsync(Guid id)
    {
        return await _context.MediaItems.FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task ReplaceMarkersAsync(Guid mediaItemId, IEnumerable<MediaItemMarker> markers)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Set<MediaItemMarker>()
                .Where(m => m.MediaItemId == mediaItemId)
                .ExecuteDeleteAsync();

            var fresh = markers
                .Select(m => new MediaItemMarker
                {
                    MediaItemId = mediaItemId,
                    Type = m.Type,
                    Start = m.Start,
                    End = m.End,
                    Order = m.Order
                })
                .ToList();

            if (fresh.Count > 0)
            {
                await _context.Set<MediaItemMarker>().AddRangeAsync(fresh);
                await _context.SaveChangesAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<List<MediaItemMarker>> GetMarkersForSeasonAsync(Guid seasonId)
    {
        return await _context.Set<MediaItemMarker>()
            .AsNoTracking()
            .Where(m => _context.Set<Episode>().Any(e => e.SeasonId == seasonId && e.Id == m.MediaItemId))
            .ToListAsync();
    }

    public async Task<List<MediaItemMarker>> GetMarkersAsync(Guid mediaItemId)
    {
        return await _context.Set<MediaItemMarker>()
            .AsNoTracking()
            .Where(m => m.MediaItemId == mediaItemId)
            .OrderBy(m => m.Type)
            .ThenBy(m => m.Order)
            .ThenBy(m => m.Start)
            .ToListAsync();
    }

    public async Task<(bool MidStinger, bool PostStinger)> GetStingerFlagsAsync(Guid mediaItemId)
    {
        var flags = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaItemId)
            .Select(m => new { m.HasMidCreditsStinger, m.HasPostCreditsStinger })
            .FirstOrDefaultAsync();
        return flags == null ? (false, false) : (flags.HasMidCreditsStinger, flags.HasPostCreditsStinger);
    }

    public async Task<MarkerCoverageVM> GetMarkerCoverageAsync(Guid libraryId)
    {
        var libraryName = await _context.MediaLibraries
            .AsNoTracking()
            .Where(l => l.Id == libraryId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync() ?? string.Empty;

        var playable = _context.MediaItems
            .AsNoTracking()
            .Where(m => m is Movie || m is Episode)
            .Where(m =>
                (m is Movie && m.LibraryId == libraryId) ||
                (m is Episode && ((Episode)m).Season.TvShow.LibraryId == libraryId));

        var counts = await playable
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WithAny = g.Sum(m => m.Markers.Any() ? 1 : 0),
                WithIntro = g.Sum(m => m.Markers.Any(k => k.Type == MarkerType.Intro) ? 1 : 0),
                WithCredits = g.Sum(m => m.Markers.Any(k => k.Type == MarkerType.Credits) ? 1 : 0),
                WithCreditsScene = g.Sum(m => m.Markers.Any(k => k.Type == MarkerType.CreditsScene) ? 1 : 0),
                WithRecap = g.Sum(m => m.Markers.Any(k => k.Type == MarkerType.Recap) ? 1 : 0),
                WithPreview = g.Sum(m => m.Markers.Any(k => k.Type == MarkerType.Preview) ? 1 : 0),
                MissingDuration = g.Sum(m => (m.Analysis == null || m.Analysis.Duration == null) ? 1 : 0)
            })
            .FirstOrDefaultAsync();

        return new MarkerCoverageVM
        {
            LibraryId = libraryId,
            LibraryName = libraryName,
            TotalItems = counts?.Total ?? 0,
            ItemsWithAnyMarker = counts?.WithAny ?? 0,
            ItemsWithIntro = counts?.WithIntro ?? 0,
            ItemsWithCredits = counts?.WithCredits ?? 0,
            ItemsWithCreditsScene = counts?.WithCreditsScene ?? 0,
            ItemsWithRecap = counts?.WithRecap ?? 0,
            ItemsWithPreview = counts?.WithPreview ?? 0,
            ItemsMissingDuration = counts?.MissingDuration ?? 0
        };
    }

    public async Task<bool> AreMarkersLockedAsync(Guid mediaItemId)
    {
        var locked = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaItemId)
            .Select(m => m.LockedFields)
            .FirstOrDefaultAsync();
        if (locked == null) return false;
        return locked.Contains("Markers", StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetMarkersLockedAsync(Guid mediaItemId, bool locked)
    {
        var item = await _context.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;
        if (locked) item.LockField("Markers");
        else item.UnlockField("Markers");
        await _context.SaveChangesAsync();
    }

    public async Task<bool> AreThumbnailsLockedAsync(Guid mediaItemId)
    {
        var locked = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaItemId)
            .Select(m => m.LockedFields)
            .FirstOrDefaultAsync();
        if (locked == null) return false;
        return locked.Contains("Thumbnails", StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetThumbnailsLockedAsync(Guid mediaItemId, bool locked)
    {
        var item = await _context.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;
        if (locked) item.LockField("Thumbnails");
        else item.UnlockField("Thumbnails");
        await _context.SaveChangesAsync();
    }

    public async Task UpdateMediaItemAsync(MediaItem item)
    {
        var tracked = _context.MediaItems.Local.FirstOrDefault(m => m.Id == item.Id);
        if (tracked != null)
        {
            _context.Entry(tracked).CurrentValues.SetValues(item);
        }
        else
        {
            _context.MediaItems.Update(item);
        }

        await _context.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => _context.SaveChangesAsync();

    public async Task AddMediaItemAsync(MediaItem item)
    {
        try
        {
            await _context.MediaItems.AddAsync(item);
            await _context.SaveChangesAsync();
            await RestoreUserDataForItemAsync(item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add MediaItem {Title}.", item.Title);
            throw;
        }
    }

    public async Task AddMediaVideosAsync(IEnumerable<MediaVideo> videos)
    {
        await _context.MediaVideos.AddRangeAsync(videos);
    }

    public Task RemoveMediaVideosAsync(IEnumerable<MediaVideo> videos)
    {
        _context.MediaVideos.RemoveRange(videos);
        return Task.CompletedTask;
    }

    public async Task AddMediaExtraAsync(MediaExtra extra)
    {
        await _context.MediaExtras.AddAsync(extra);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteMediaByFilePathAsync(string filePath)
    {
        var item = await _context.MediaItems
            .Include(m => m.MediaParts)
            .FirstOrDefaultAsync(m => m.MediaParts.Any(p => p.FilePath == filePath));

        if (item != null)
        {
            var partToRemove = item.MediaParts.FirstOrDefault(p => p.FilePath == filePath);

            if (partToRemove != null)
            {
                if (item.MediaParts.Count <= 1)
                {
                    _context.MediaItems.Remove(item);
                }
                else
                {
                    item.MediaParts.Remove(partToRemove);
                    _context.Entry(partToRemove).State = EntityState.Deleted;
                }

                await _context.SaveChangesAsync();
            }
        }
    }

    public async Task MarkMediaMissingByFilePathAsync(string filePath)
    {
        var item = await _context.MediaItems
            .Include(m => m.MediaParts)
            .FirstOrDefaultAsync(m => m.MediaParts.Any(p => p.FilePath == filePath));

        if (item == null) return;

        var partToRemove = item.MediaParts.FirstOrDefault(p => p.FilePath == filePath);
        if (partToRemove == null) return;

        item.MediaParts.Remove(partToRemove);
        _context.Entry(partToRemove).State = EntityState.Deleted;

        if (item.MediaParts.Count == 0)
        {
            if (item is Track)
            {
                _context.MediaItems.Remove(item);
            }
            else if (item.MissingSince == null)
            {
                item.MissingSince = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
    }

    public Task<List<TrashMediaItemVM>> GetMissingMediaAsync() =>
        _context.MediaItems
            .AsNoTracking()
            .Where(m => m.MissingSince != null)
            .OrderByDescending(m => m.MissingSince)
            .Select(TrashMediaItemVM.Projection)
            .ToListAsync();

    public Task<List<Guid>> GetExpiredMissingMediaIdsAsync(DateTime cutoffUtc) =>
        _context.MediaItems
            .AsNoTracking()
            .Where(m => m.MissingSince != null && m.MissingSince < cutoffUtc)
            .Select(m => m.Id)
            .ToListAsync();

    public Task RestoreMissingMediaAsync(Guid id) =>
        _context.MediaItems
            .Where(m => m.Id == id && m.MissingSince != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.MissingSince, (DateTime?)null));

    public async Task DeleteMediaItemAsync(Guid id)
    {
        var item = await _context.MediaItems.FindAsync(id);
        if (item != null)
        {
            await ArchiveUserDataForItemAsync(id);
            _context.MediaItems.Remove(item);
            await _context.SaveChangesAsync();
        }
    }

    private async Task<string?> GetContentKeyAsync(Guid id)
    {
        var info = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                Type = m is Movie ? "movie"
                    : m is TvShow ? "show"
                    : m is Season ? "season"
                    : m is Episode ? "episode"
                    : "other",
                m.TmdbId,
                m.ImdbId,
                m.TvdbId,
                SeasonNumber = m is Season ? ((Season)m).SeasonNumber
                    : m is Episode ? ((Episode)m).Season.SeasonNumber
                    : (int?)null,
                EpisodeNumber = m is Episode ? ((Episode)m).EpisodeNumber : (int?)null,
                SeriesTmdbId = m is Season ? ((Season)m).TvShow.TmdbId
                    : m is Episode ? ((Episode)m).Season.TvShow.TmdbId
                    : null,
                SeriesImdbId = m is Season ? ((Season)m).TvShow.ImdbId
                    : m is Episode ? ((Episode)m).Season.TvShow.ImdbId
                    : null,
                SeriesTvdbId = m is Season ? ((Season)m).TvShow.TvdbId
                    : m is Episode ? ((Episode)m).Season.TvShow.TvdbId
                    : null
            })
            .FirstOrDefaultAsync();

        if (info == null) return null;

        return ContentIdentity.Compute(
            info.Type, info.TmdbId, info.ImdbId, info.TvdbId,
            info.SeasonNumber, info.EpisodeNumber,
            info.SeriesTmdbId, info.SeriesImdbId, info.SeriesTvdbId);
    }

    private async Task ArchiveUserDataForItemAsync(Guid id)
    {
        var contentKey = await GetContentKeyAsync(id);
        if (contentKey == null) return;

        var ratings = await _context.UserMediaRatings.AsNoTracking().Where(r => r.MediaItemId == id).ToListAsync();
        var states = await _context.UserMediaStates.AsNoTracking().Where(s => s.MediaItemId == id).ToListAsync();
        if (ratings.Count == 0 && states.Count == 0) return;

        var profileIds = ratings.Select(r => r.ProfileId).Concat(states.Select(s => s.ProfileId)).Distinct().ToList();

        var existing = await _context.PreservedUserMediaData
            .Where(p => p.ContentKey == contentKey && profileIds.Contains(p.ProfileId))
            .ToListAsync();
        var byProfile = existing.ToDictionary(p => p.ProfileId);

        foreach (var profileId in profileIds)
        {
            if (!byProfile.TryGetValue(profileId, out var archive))
            {
                archive = new PreservedUserMediaData { ProfileId = profileId, ContentKey = contentKey };
                _context.PreservedUserMediaData.Add(archive);
                byProfile[profileId] = archive;
            }
            archive.ArchivedAt = DateTime.UtcNow;

            var rating = ratings.FirstOrDefault(r => r.ProfileId == profileId);
            if (rating != null && (archive.RatedAt == null || rating.RatedAt >= archive.RatedAt))
            {
                archive.Rating = rating.Rating;
                archive.RatedAt = rating.RatedAt;
            }

            var state = states.FirstOrDefault(s => s.ProfileId == profileId);
            if (state != null && (!archive.HasState || archive.LastPlayedAt == null || state.LastPlayedAt >= archive.LastPlayedAt))
            {
                archive.HasState = true;
                archive.ResumePositionSeconds = state.ResumePositionSeconds;
                archive.IsPlayed = state.IsPlayed;
                archive.IsHiddenFromContinueWatching = state.IsHiddenFromContinueWatching;
                archive.LastPlayedAt = state.LastPlayedAt;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task RestoreUserDataForItemAsync(Guid id)
    {
        if (!await _context.PreservedUserMediaData.AnyAsync()) return;

        var contentKey = await GetContentKeyAsync(id);
        if (contentKey == null) return;

        var archives = await _context.PreservedUserMediaData.Where(p => p.ContentKey == contentKey).ToListAsync();
        if (archives.Count == 0) return;

        var profileIds = archives.Select(p => p.ProfileId).ToList();
        var existingRatings = await _context.UserMediaRatings
            .Where(r => r.MediaItemId == id && profileIds.Contains(r.ProfileId)).ToListAsync();
        var existingStates = await _context.UserMediaStates
            .Where(s => s.MediaItemId == id && profileIds.Contains(s.ProfileId)).ToListAsync();

        foreach (var archive in archives)
        {
            if (archive.Rating.HasValue)
            {
                var rating = existingRatings.FirstOrDefault(r => r.ProfileId == archive.ProfileId);
                if (rating == null)
                {
                    _context.UserMediaRatings.Add(new UserMediaRating
                    {
                        ProfileId = archive.ProfileId,
                        MediaItemId = id,
                        Rating = archive.Rating.Value,
                        RatedAt = archive.RatedAt ?? DateTime.UtcNow
                    });
                }
                else if (archive.RatedAt.HasValue && archive.RatedAt > rating.RatedAt)
                {
                    rating.Rating = archive.Rating.Value;
                    rating.RatedAt = archive.RatedAt.Value;
                }
            }

            if (archive.HasState)
            {
                var state = existingStates.FirstOrDefault(s => s.ProfileId == archive.ProfileId);
                if (state == null)
                {
                    _context.UserMediaStates.Add(new UserMediaState
                    {
                        ProfileId = archive.ProfileId,
                        MediaItemId = id,
                        ResumePositionSeconds = archive.ResumePositionSeconds,
                        IsPlayed = archive.IsPlayed,
                        IsHiddenFromContinueWatching = archive.IsHiddenFromContinueWatching,
                        LastPlayedAt = archive.LastPlayedAt ?? DateTime.UtcNow
                    });
                }
                else if (archive.LastPlayedAt.HasValue && archive.LastPlayedAt > state.LastPlayedAt)
                {
                    state.ResumePositionSeconds = archive.ResumePositionSeconds;
                    state.IsPlayed = archive.IsPlayed;
                    state.IsHiddenFromContinueWatching = archive.IsHiddenFromContinueWatching;
                    state.LastPlayedAt = archive.LastPlayedAt.Value;
                }
            }

            _context.PreservedUserMediaData.Remove(archive);
        }

        await _context.SaveChangesAsync();
    }

    public async Task AddMediaPartAsync(MediaPart part)
    {
        await _context.MediaParts.AddAsync(part);
        await _context.SaveChangesAsync();

        // Adding a part can change the item's best resolution/audio/HDR, so clear
        // LastOverlayGeneratedAt to flag it for overlay regeneration on the next
        // sync (the gate otherwise only re-runs on metadata/template changes).
        // Also clear the missing flag if the file returned.
        await _context.MediaItems
            .Where(m => m.Id == part.MediaItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.LastOverlayGeneratedAt, (DateTime?)null)
                .SetProperty(m => m.MissingSince, (DateTime?)null));
    }

    public async Task SyncItemEditionFromPartsAsync(Guid mediaItemId)
    {
        var item = await _context.MediaItems
            .Include(m => m.MediaParts)
            .FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;

        var bestEdition = item.MediaParts
            .OrderByDescending(p => ResolutionRank(p.Resolution))
            .ThenBy(p => p.Id)
            .Select(p => p.Edition)
            .FirstOrDefault();

        if (item.Edition != bestEdition)
        {
            item.Edition = bestEdition;
            await _context.SaveChangesAsync();
        }
    }

    private static int ResolutionRank(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return 0;
        var digits = new string(resolution.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    public async Task AddMediaCastMembersAsync(IEnumerable<MediaCastMember> castMembers)
    {
        if (castMembers.Any())
        {
            await _context.MediaCastMembers.AddRangeAsync(castMembers);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemoveMediaCastMembersAsync(IEnumerable<MediaCastMember> castMembers)
    {
        if (castMembers.Any())
        {
            _context.MediaCastMembers.RemoveRange(castMembers);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> MediaExistsByExternalIdAsync(string externalId, string type)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return false;

        if (type == "Movie")
        {
            return await _context.MediaItems.OfType<Movie>()
                .AnyAsync(m => m.TmdbId == externalId);
        }
        else if (type == "TvShow")
        {
            return await _context.MediaItems.OfType<TvShow>()
                .AnyAsync(m => m.TmdbId == externalId);
        }

        return await _context.MediaItems.AnyAsync(m => m.TmdbId == externalId);
    }

    public async Task<HashSet<string>> GetExistingExternalIdsAsync(IEnumerable<string> externalIds, string type)
    {
        var ids = externalIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<string>();

        IQueryable<MediaItem> query = type switch
        {
            "Movie" => _context.MediaItems.OfType<Movie>(),
            "TvShow" => _context.MediaItems.OfType<TvShow>(),
            _ => _context.MediaItems
        };

        var found = await query
            .Where(m => m.TmdbId != null && ids.Contains(m.TmdbId))
            .Select(m => m.TmdbId ?? string.Empty)
            .ToListAsync();

        return found.ToHashSet();
    }

    public async Task<Dictionary<string, Guid>> GetLibraryIdsByTmdbIdsAsync(IEnumerable<string> tmdbIds)
    {
        var items = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.TmdbId != null && tmdbIds.Contains(m.TmdbId))
            .Select(m => new { m.TmdbId, m.LibraryId })
            .ToListAsync();

        var dict = new Dictionary<string, Guid>();
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.TmdbId))
            {
                dict.TryAdd(item.TmdbId, item.LibraryId);
            }
        }
        return dict;
    }

    public async Task<List<MediaItem>> GetItemsPendingOverlayGenerationAsync(Guid libraryId, DateTime maxTemplateUpdatedDate)
    {
        var query = _context.MediaItems
            .AsNoTracking()
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.AudioTracks)
            .Include(m => m.MediaParts)
                .ThenInclude(p => p.VideoTracks)
            .AsQueryable();

        if (libraryId != Guid.Empty)
        {
            query = query.Where(m => m.LibraryId == libraryId);
        }

        var validMediaTypes = await _context.OverlayTemplates
            .Where(t => t.TargetLibraryId == null || t.TargetLibraryId == Guid.Empty || t.TargetLibraryId == libraryId)
            .Select(t => t.TargetMediaType)
            .ToListAsync();

        var hasMovieTemplate = validMediaTypes.Contains("Movie");
        var hasTvShowTemplate = validMediaTypes.Contains("TvShow");
        var hasSeasonTemplate = validMediaTypes.Contains("Season");
        var hasEpisodeTemplate = validMediaTypes.Contains("Episode");

        return await query
            .Where(m =>
                // Revert: previously overlaid, but its type no longer has a template.
                (m.LastOverlayGeneratedAt != null && (
                    (m is Movie && !hasMovieTemplate) ||
                    (m is TvShow && !hasTvShowTemplate) ||
                    (m is Season && !hasSeasonTemplate) ||
                    (m is Episode && !hasEpisodeTemplate)
                )) ||
                // Generate: only for types that HAVE a template, when never
                // overlaid / metadata is newer / the template changed. Without
                // the type gate every never-overlaid item on the server is
                // returned (then no-ops), so a movie-only template would walk
                // the entire Shows library.
                (
                    (
                        (m is Movie && hasMovieTemplate) ||
                        (m is TvShow && hasTvShowTemplate) ||
                        (m is Season && hasSeasonTemplate) ||
                        (m is Episode && hasEpisodeTemplate)
                    ) &&
                    (
                        m.LastOverlayGeneratedAt == null ||
                        m.LastMetadataRefresh > m.LastOverlayGeneratedAt ||
                        m.LastOverlayGeneratedAt < maxTemplateUpdatedDate
                    )
                )
            )
            .ToListAsync();
    }

    public async Task<bool> AnyItemHasOverlayAppliedAsync(Guid libraryId)
    {
        var query = _context.MediaItems
            .AsNoTracking()
            .Where(m => m.LastOverlayGeneratedAt != null);

        if (libraryId != Guid.Empty)
        {
            query = query.Where(m => m.LibraryId == libraryId);
        }

        return await query.AnyAsync();
    }

    public async Task<HashSet<string>> GetReferencedOverlayFileNamesAsync()
    {
        const string overlayMarker = "_overlay_";

        var urls = await _context.MediaItems
            .AsNoTracking()
            .Where(m =>
                (m.PosterUrl != null && m.PosterUrl.Contains(overlayMarker)) ||
                (m.BackgroundUrl != null && m.BackgroundUrl.Contains(overlayMarker)))
            .Select(m => new { m.PosterUrl, m.BackgroundUrl })
            .ToListAsync();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in urls)
        {
            AddOverlayFileName(names, pair.PosterUrl);
            AddOverlayFileName(names, pair.BackgroundUrl);
        }
        return names;
    }

    private static void AddOverlayFileName(HashSet<string> names, string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains("_overlay_", StringComparison.Ordinal)) return;
        names.Add(url.Split('/').Last());
    }

    public async Task<string?> GetParentContentRatingAsync(Guid mediaItemId)
    {
        var item = await _context.MediaItems.FindAsync(mediaItemId);

        if (item is Episode ep)
        {
            var season = await _context.Set<Season>()
                .Include(s => s.TvShow)
                .FirstOrDefaultAsync(s => s.Id == ep.SeasonId);
            return season?.TvShow?.ContentRating;
        }

        if (item is Season s)
        {
            var show = await _context.Set<TvShow>()
                .FirstOrDefaultAsync(t => t.Id == s.TvShowId);
            return show?.ContentRating;
        }

        return null;
    }
}