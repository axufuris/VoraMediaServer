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
            .Where(p => p.MediaItem.LibraryId == libraryId)
            .Select(p => p.FilePath)
            .ToListAsync();
        return new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
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
        var item = await _context.MediaItems
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
            .FirstOrDefaultAsync(m => m.Id == id);

        if (item is Episode episode)
        {
            await _context.Entry(episode).Reference(e => e.Season).LoadAsync();
            if (episode.Season != null)
            {
                await _context.Entry(episode.Season).Reference(s => s.TvShow).LoadAsync();
                if (episode.Season.TvShow != null)
                {
                    await _context.Entry(episode.Season.TvShow).Collection(t => t.Cast).Query().Include(c => c.Actor).LoadAsync();
                }
            }
        }
        return item;
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

    public async Task<MediaItem?> GetForBasicUpdateAsync(Guid id)
    {
        return await _context.MediaItems.FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task UpdateSilenceDetectionsAsync(Guid mediaItemId, TimeSpan? introStart, TimeSpan? introEnd, TimeSpan? creditsStart, TimeSpan? duration)
    {
        var rowsAffected = await _context.Set<MediaItemAnalysis>()
            .Where(a => a.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Duration, duration)
                .SetProperty(a => a.IntroStart, introStart)
                .SetProperty(a => a.IntroEnd, introEnd)
                .SetProperty(a => a.CreditsStart, creditsStart));

        if (rowsAffected == 0)
        {
            var newAnalysis = new MediaItemAnalysis
            {
                MediaItemId = mediaItemId,
                IntroStart = introStart,
                IntroEnd = introEnd,
                CreditsStart = creditsStart,
                Duration = duration
            };

            await _context.Set<MediaItemAnalysis>().AddAsync(newAnalysis);
            await _context.SaveChangesAsync();
        }
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