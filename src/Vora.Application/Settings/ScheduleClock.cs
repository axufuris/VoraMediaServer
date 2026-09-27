namespace Vora.Application.Settings;

// Every scheduled job compares "the time now" against a time an admin typed
// into the settings page. That comparison needs a zone, and the container's own
// clock is the wrong one: a Docker image has no TZ unless someone sets it, so
// DateTime.Now is UTC and "run at 02:00" fired at 02:00 UTC — for an admin in
// UTC-5 that is 9pm the previous evening, on a page that says "each night".
public static class ScheduleClock
{
    // Resolution order: the admin's configured zone, then the container's own
    // (which honours a TZ environment variable), then UTC. An id that does not
    // resolve falls through rather than throwing — a typo in a settings field
    // must not stop every scheduled job on the server.
    public static TimeZoneInfo Resolve(string? configuredTimeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(configuredTimeZoneId))
        {
            if (TryFind(configuredTimeZoneId.Trim(), out var configured)) return configured;
        }

        return TimeZoneInfo.Local;
    }

    public static bool IsKnown(string? timeZoneId) =>
        string.IsNullOrWhiteSpace(timeZoneId) || TryFind(timeZoneId.Trim(), out _);

    public static DateTime Now(string? configuredTimeZoneId, DateTime utcNow) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcNow, Resolve(configuredTimeZoneId));

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
