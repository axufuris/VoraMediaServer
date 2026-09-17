using Microsoft.EntityFrameworkCore;
using Vora.Application.Calendar;
using Vora.Application.Calendar.Dtos;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Requests;

namespace Vora.Infrastructure.Persistence.Repositories;

public class CalendarRepository(VoraDbContext context) : ICalendarRepository
{
    public async Task<List<CalendarMovieSourceDto>> GetMoviesReleasingInRangeAsync(DateTime startDate, DateTime endDate) =>
        await context.Set<Movie>()
            .AsNoTracking()
            .Where(m => (m.TheatricalReleaseDate >= startDate && m.TheatricalReleaseDate <= endDate)
                || (m.DigitalReleaseDate >= startDate && m.DigitalReleaseDate <= endDate))
            .Select(CalendarMovieSourceDto.Projection)
            .ToListAsync();

    public async Task<List<CalendarShowSourceDto>> GetActiveShowsWithUpcomingEpisodesAsync() =>
        await context.Set<TvShow>()
            .AsNoTracking()
            .Where(t => t.Status != "Ended"
                && !string.IsNullOrEmpty(t.UpcomingEpisodesJson)
                && t.UpcomingEpisodesJson != "[]")
            .Select(CalendarShowSourceDto.Projection)
            .ToListAsync();

    public async Task<List<CalendarRequestSourceDto>> GetRequestsReleasingInRangeAsync(DateTime startDate, DateTime endDate) =>
        await context.Set<MediaRequest>()
            .AsNoTracking()
            .Where(r => r.ExpectedReleaseDate >= startDate && r.ExpectedReleaseDate <= endDate)
            .Select(CalendarRequestSourceDto.Projection)
            .ToListAsync();

    public async Task<List<CalendarWatchlistSourceDto>> GetWatchlistItemsReleasingInRangeAsync(DateTime startDate, DateTime endDate) =>
        await context.Set<UserWatchlistItem>()
            .AsNoTracking()
            .Where(w => w.ExpectedReleaseDate >= startDate && w.ExpectedReleaseDate <= endDate)
            .Select(CalendarWatchlistSourceDto.Projection)
            .ToListAsync();
}
