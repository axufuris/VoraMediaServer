using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Media;
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

    public async Task<Guid?> GetMovieIdByTitleAndYearAsync(string title, int? year, Guid libraryId)
    {
        return await _context.Set<Movie>()
            .Where(m => m.Title == title && m.LibraryId == libraryId && (year == null || m.ReleaseDate!.Value.Year == year))
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync();
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
                ))
            .OrderBy(m => m is TvShow ? 0 : m is Movie ? 0 : m is Season ? 1 : 2)
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

    public async Task AddMediaItemAsync(MediaItem item)
    {
        try
        {
            await _context.MediaItems.AddAsync(item);
            await _context.SaveChangesAsync();
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

    public async Task DeleteMediaItemAsync(Guid id)
    {
        var item = await _context.MediaItems.FindAsync(id);
        if (item != null)
        {
            _context.MediaItems.Remove(item);
            await _context.SaveChangesAsync();
        }
    }

    public async Task AddMediaPartAsync(MediaPart part)
    {
        await _context.MediaParts.AddAsync(part);
        await _context.SaveChangesAsync();
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
                (m.LastOverlayGeneratedAt != null && (
                    (m is Movie && !hasMovieTemplate) ||
                    (m is TvShow && !hasTvShowTemplate) ||
                    (m is Season && !hasSeasonTemplate) ||
                    (m is Episode && !hasEpisodeTemplate)
                )) ||
                m.LastOverlayGeneratedAt == null ||
                m.LastMetadataRefresh > m.LastOverlayGeneratedAt ||
                m.LastOverlayGeneratedAt < maxTemplateUpdatedDate
            )
            .ToListAsync();
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