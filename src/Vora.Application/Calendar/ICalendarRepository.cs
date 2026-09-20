using Vora.Application.Calendar.Dtos;

namespace Vora.Application.Calendar;

public interface ICalendarRepository
{
    Task<List<CalendarMovieSourceDto>> GetMoviesReleasingInRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<CalendarShowSourceDto>> GetActiveShowsWithUpcomingEpisodesAsync();
    Task<List<CalendarRequestSourceDto>> GetRequestsReleasingInRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<CalendarWatchlistSourceDto>> GetWatchlistItemsReleasingInRangeAsync(DateTime startDate, DateTime endDate);
}
