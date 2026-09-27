using Microsoft.EntityFrameworkCore;
using Vora.Application.Calendar;
using Vora.Application.Calendar.Dtos;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Requests;

namespace Vora.Infrastructure.Persistence.Repositories;

public class CalendarRepository(VoraDbContext context) : ICalendarRepository
{
    // A release date is a whole day, so the window is compared in days: an item
    // released on the last day of the view belongs in it regardless of the time
    // of day the caller's range happens to end at.
    public async Task<List<CalendarMovieSourceDto>> GetMoviesReleasingInRangeAsync(DateTime startDate, DateTime endDate)
    {
        var firstDay = DateOnly.FromDateTime(startDate);
        var lastDay = DateOnly.FromDateTime(endDate);

        return await context.Set<Movie>()
            .AsNoTracking()
            .Where(m => (m.TheatricalReleaseDate >= firstDay && m.TheatricalReleaseDate <= lastDay)
                || (m.DigitalReleaseDate >= firstDay && m.DigitalReleaseDate <= lastDay))
            .Select(CalendarMovieSourceDto.Projection)
            .ToListAsync();
    }

    public async Task<List<CalendarShowSourceDto>> GetActiveShowsWithUpcomingEpisodesAsync() =>
        await context.Set<TvShow>()
            .AsNoTracking()
            .Where(t => t.Status != "Ended"
                && !string.IsNullOrEmpty(t.UpcomingEpisodesJson)
                && t.UpcomingEpisodesJson != "[]")
            .Select(CalendarShowSourceDto.Projection)
            .ToListAsync();

    public async Task<List<CalendarRequestSourceDto>> GetRequestsReleasingInRangeAsync(DateTime startDate, DateTime endDate)
    {
        var firstDay = DateOnly.FromDateTime(startDate);
        var lastDay = DateOnly.FromDateTime(endDate);

        return await context.Set<MediaRequest>()
            .AsNoTracking()
            .Where(r => r.ExpectedReleaseDate >= firstDay && r.ExpectedReleaseDate <= lastDay)
            .Select(CalendarRequestSourceDto.Projection)
            .ToListAsync();
    }

    public async Task<List<CalendarWatchlistSourceDto>> GetWatchlistItemsReleasingInRangeAsync(DateTime startDate, DateTime endDate)
    {
        var firstDay = DateOnly.FromDateTime(startDate);
        var lastDay = DateOnly.FromDateTime(endDate);

        return await context.Set<UserWatchlistItem>()
            .AsNoTracking()
            .Where(w => w.ExpectedReleaseDate >= firstDay && w.ExpectedReleaseDate <= lastDay)
            .Select(CalendarWatchlistSourceDto.Projection)
            .ToListAsync();
    }
}
