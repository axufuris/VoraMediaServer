namespace Vora.Application.Backups;

public static class BackupScheduleEvaluator
{
    private static readonly TimeSpan GapStep = TimeSpan.FromMinutes(1);

    public static DateTime? GetNextRunUtc(BackupSettings settings, DateTime afterUtc, TimeZoneInfo zone)
    {
        if (!IsScheduled(settings)) return null;

        var after = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        var localAfter = TimeZoneInfo.ConvertTimeFromUtc(after, zone);

        var candidate = FirstCandidate(settings, localAfter);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var candidateUtc = ToUtc(candidate, zone);
            if (candidateUtc > after) return candidateUtc;
            candidate = Advance(settings, candidate);
        }

        return ToUtc(candidate, zone);
    }

    public static DateTime? GetDisplayedNextRunUtc(BackupSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (!IsScheduled(settings)) return null;
        if (settings.LastSuccessfulRunUtc == null) return nowUtc;

        var next = GetNextRunUtc(settings, settings.LastSuccessfulRunUtc.Value, zone);
        return next == null || next.Value < nowUtc ? nowUtc : next;
    }

    public static bool IsDue(BackupSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (!IsScheduled(settings)) return false;
        if (settings.LastSuccessfulRunUtc == null) return true;

        var next = GetNextRunUtc(settings, settings.LastSuccessfulRunUtc.Value, zone);
        return next != null && nowUtc >= next.Value;
    }

    private static bool IsScheduled(BackupSettings settings) =>
        settings.AutoBackupEnabled && settings.Cadence != BackupCadence.Off;

    private static DateTime FirstCandidate(BackupSettings settings, DateTime localAfter)
    {
        var todayAt = new DateTime(localAfter.Year, localAfter.Month, localAfter.Day, Math.Clamp(settings.Hour, 0, 23), Math.Clamp(settings.Minute, 0, 59), 0, DateTimeKind.Unspecified);

        return settings.Cadence switch
        {
            BackupCadence.Weekly => todayAt.AddDays(((int)settings.DayOfWeek - (int)localAfter.DayOfWeek + 7) % 7),
            BackupCadence.Monthly => new DateTime(localAfter.Year, localAfter.Month, Math.Clamp(settings.DayOfMonth, 1, 28), todayAt.Hour, todayAt.Minute, 0, DateTimeKind.Unspecified),
            _ => todayAt
        };
    }

    private static DateTime Advance(BackupSettings settings, DateTime candidate) => settings.Cadence switch
    {
        BackupCadence.Weekly => candidate.AddDays(7),
        BackupCadence.Monthly => candidate.AddMonths(1),
        _ => candidate.AddDays(1)
    };

    private static DateTime ToUtc(DateTime localWallTime, TimeZoneInfo zone)
    {
        var wallTime = localWallTime;
        while (zone.IsInvalidTime(wallTime))
        {
            wallTime = wallTime.Add(GapStep);
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallTime, zone);
    }
}
