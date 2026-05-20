using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ICalendarProvider : IVoraPlugin
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    bool IsSystemPlugin { get; }

    Task<IEnumerable<CalendarEventDto>> GetEventsAsync(DateTime startDate, DateTime endDate);
}
