using Vora.Application.Backups.ViewModels;

namespace Vora.Application.Backups;

public static class BackupSettingsMapper
{
    public static BackupSettingsVM ToVM(BackupSettings s, string effectiveDirectory, List<AvailableSectionVM> available, TimeZoneInfo scheduleZone, DateTime nowUtc)
    {
        return new BackupSettingsVM
        {
            AutoBackupEnabled = s.AutoBackupEnabled,
            Cadence = s.Cadence.ToString(),
            Hour = s.Hour,
            Minute = s.Minute,
            DayOfWeek = s.DayOfWeek.ToString(),
            DayOfMonth = s.DayOfMonth,
            MaxToKeep = s.MaxToKeep,
            OverrideDirectory = s.OverrideDirectory,
            EffectiveDirectory = effectiveDirectory,
            LastSuccessfulRunUtc = s.LastSuccessfulRunUtc,
            NextScheduledRunUtc = BackupScheduleEvaluator.GetDisplayedNextRunUtc(s, nowUtc, scheduleZone),
            ScheduleTimeZone = scheduleZone.Id,
            IncludedSectionKeys = BackupSectionSelection.IncludedKeys(s, available.Select(a => a.Key)),
            AvailableSections = available
        };
    }

    public static BackupSettings FromVM(BackupSettingsVM vm, BackupSettings existing, IEnumerable<string> availableKeys)
    {
        return new BackupSettings
        {
            AutoBackupEnabled = vm.AutoBackupEnabled,
            Cadence = Enum.TryParse<BackupCadence>(vm.Cadence, true, out var cadence) ? cadence : BackupCadence.Daily,
            Hour = Math.Clamp(vm.Hour, 0, 23),
            Minute = Math.Clamp(vm.Minute, 0, 59),
            DayOfWeek = Enum.TryParse<DayOfWeek>(vm.DayOfWeek, true, out var dow) ? dow : DayOfWeek.Sunday,
            DayOfMonth = Math.Clamp(vm.DayOfMonth <= 0 ? 1 : vm.DayOfMonth, 1, 28),
            MaxToKeep = Math.Max(1, vm.MaxToKeep),
            OverrideDirectory = string.IsNullOrWhiteSpace(vm.OverrideDirectory) ? null : vm.OverrideDirectory,
            LastSuccessfulRunUtc = existing.LastSuccessfulRunUtc,
            ExcludedSectionKeys = BackupSectionSelection.ExcludedKeys(vm.IncludedSectionKeys, availableKeys)
        };
    }
}
