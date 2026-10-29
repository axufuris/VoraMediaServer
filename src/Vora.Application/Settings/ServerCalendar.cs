namespace Vora.Application.Settings;

public interface IServerCalendar
{
    Task<DateOnly> TodayAsync();
}

public class ServerCalendar(ISystemSettingsRepository settings, TimeProvider time) : IServerCalendar
{
    public async Task<DateOnly> TodayAsync() =>
        ScheduleClock.Today((await settings.GetSettingsAsync()).ScheduleTimeZone, time.GetUtcNow().UtcDateTime);
}
