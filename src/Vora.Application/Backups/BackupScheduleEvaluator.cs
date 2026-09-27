namespace Vora.Application.Backups;

public static class BackupScheduleEvaluator
{
    public static DateTime? GetNextRunUtc(BackupSettings settings, DateTime nowUtc)
    {
        if (!settings.AutoBackupEnabled || settings.Cadence == BackupCadence.Off) return null;

        var localNow = nowUtc.ToLocalTime();
        var todayAt = new DateTime(localNow.Year, localNow.Month, localNow.Day, settings.Hour, settings.Minute, 0, DateTimeKind.Local);

        DateTime nextLocal = settings.Cadence switch
        {
            BackupCadence.Daily => NextDaily(todayAt, localNow),
            BackupCadence.Weekly => NextWeekly(todayAt, localNow, settings.DayOfWeek),
            BackupCadence.Monthly => NextMonthly(localNow, settings),
            _ => todayAt
        };

        return nextLocal.ToUniversalTime();
    }

    public static bool IsDue(BackupSettings settings, DateTime nowUtc)
    {
        if (!settings.AutoBackupEnabled || settings.Cadence == BackupCadence.Off) return false;

        var next = GetNextRunUtc(settings, settings.LastSuccessfulRunUtc ?? DateTime.MinValue);
        if (next == null) return false;

        if (settings.LastSuccessfulRunUtc == null) return true;
        return nowUtc >= next.Value;
    }

    private static DateTime NextDaily(DateTime todayAt, DateTime localNow)
    {
        return todayAt > localNow ? todayAt : todayAt.AddDays(1);
    }

    private static DateTime NextWeekly(DateTime todayAt, DateTime localNow, DayOfWeek targetDow)
    {
        var daysUntil = ((int)targetDow - (int)localNow.DayOfWeek + 7) % 7;
        var candidate = todayAt.AddDays(daysUntil);
        if (candidate <= localNow) candidate = candidate.AddDays(7);
        return candidate;
    }

    private static DateTime NextMonthly(DateTime localNow, BackupSettings settings)
    {
        var day = Math.Clamp(settings.DayOfMonth, 1, 28);
        var candidate = new DateTime(localNow.Year, localNow.Month, day, settings.Hour, settings.Minute, 0, DateTimeKind.Local);
        if (candidate <= localNow)
        {
            candidate = candidate.AddMonths(1);
        }
        return candidate;
    }
}
