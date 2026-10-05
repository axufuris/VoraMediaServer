namespace Vora.Application.Backups;

public static class BackupSectionSelection
{
    private static readonly string[] SectionsBeforeExclusionList =
    [
        "settings.server",
        "settings.plugins",
        "settings.data-protection",
        "templates.client-schedules",
        "templates.email",
        "templates.overlay",
        "library.smart-lists",
        "library.dedupe-rules",
        "iptv.playlists",
        "iptv.epg-sources",
        "iptv.tuner-profiles",
        "iptv.recording-schedules",
        "discovery.rows",
        "discovery.request-servers",
        "users.profiles",
        "users.devices",
        "users.watch-history",
        "users.ratings",
        "users.external-connections",
        "users.channel-favorites"
    ];

    public static void UpgradeLegacyInclusionList(BackupSettings settings)
    {
        if (settings.IncludedSectionKeys == null) return;

        if (settings.IncludedSectionKeys.Count > 0)
        {
            var included = new HashSet<string>(settings.IncludedSectionKeys, StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(settings.ExcludedSectionKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var key in SectionsBeforeExclusionList.Where(k => !included.Contains(k)))
            {
                excluded.Add(key);
            }
            settings.ExcludedSectionKeys = excluded.Count == 0 ? null : excluded.ToList();
        }

        settings.IncludedSectionKeys = null;
    }

    public static bool IsIncluded(BackupSettings settings, string sectionKey) =>
        settings.ExcludedSectionKeys == null
        || !settings.ExcludedSectionKeys.Contains(sectionKey, StringComparer.OrdinalIgnoreCase);

    public static List<string>? IncludedKeys(BackupSettings settings, IEnumerable<string> availableKeys)
    {
        var available = availableKeys.ToList();
        var included = available.Where(k => IsIncluded(settings, k)).ToList();
        return included.Count == available.Count ? null : included;
    }

    public static List<string>? ExcludedKeys(List<string>? includedKeys, IEnumerable<string> availableKeys)
    {
        if (includedKeys == null || includedKeys.Count == 0) return null;

        var included = new HashSet<string>(includedKeys, StringComparer.OrdinalIgnoreCase);
        var excluded = availableKeys.Where(k => !included.Contains(k)).ToList();
        return excluded.Count == 0 ? null : excluded;
    }
}
