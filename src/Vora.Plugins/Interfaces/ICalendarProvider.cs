using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ICalendarProvider : IVoraPlugin
{
    Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
}
