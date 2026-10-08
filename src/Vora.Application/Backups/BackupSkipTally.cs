using System.Globalization;

namespace Vora.Application.Backups;

public enum BackupSkipReason
{
    MissingItem,
    MissingProfile,
    MissingUser,
    MissingDevice,
    MissingLibrary,
    MissingCollection,
    MissingChannel,
    MissingIptvPlaylist,
    MissingPodcast,
    MissingRecordingSchedule,
    MissingFile,
    Duplicate
}

public readonly record struct BackupRowNoun(string Singular, string Plural);

public sealed class BackupSkipTally
{
    private readonly Dictionary<(BackupRowNoun Noun, BackupSkipReason Reason), int> _counts = new();
    private readonly List<(BackupRowNoun Noun, BackupSkipReason Reason)> _order = new();

    public int Total => _counts.Values.Sum();

    public void Skip(BackupRowNoun noun, BackupSkipReason reason, int count = 1)
    {
        if (count <= 0) return;
        var key = (noun, reason);
        if (_counts.TryGetValue(key, out var existing))
        {
            _counts[key] = existing + count;
            return;
        }
        _counts[key] = count;
        _order.Add(key);
    }

    public List<string> Warnings() => _order.Select(k => Describe(k.Noun, k.Reason, _counts[k])).ToList();

    public BackupSectionImportResult ToResult(int rowsImported) => new()
    {
        RowsImported = rowsImported,
        RowsSkipped = Total,
        Warnings = Warnings()
    };

    public static string Describe(BackupRowNoun noun, BackupSkipReason reason, int count)
    {
        var one = count == 1;
        var subject = $"{count.ToString("N0", CultureInfo.InvariantCulture)} {(one ? noun.Singular : noun.Plural)}";
        var was = one ? "was" : "were";
        var its = one ? "its" : "their";

        return reason switch
        {
            BackupSkipReason.MissingItem =>
                $"{subject} {was} skipped because {its} item isn't on this server. On a rebuilt server, scan the libraries first and then restore this section again.",
            BackupSkipReason.MissingProfile =>
                $"{subject} {was} skipped because {its} profile isn't on this server. Restore User Accounts & Profiles too.",
            BackupSkipReason.MissingUser =>
                $"{subject} {was} skipped because {its} user account isn't on this server. Restore User Accounts & Profiles too.",
            BackupSkipReason.MissingDevice =>
                $"{subject} {was} skipped because {its} device isn't on this server. Restore Devices & Per-Device Settings too.",
            BackupSkipReason.MissingLibrary =>
                $"{subject} {was} skipped because {its} library isn't on this server.",
            BackupSkipReason.MissingCollection =>
                $"{subject} {was} skipped because {its} collection isn't on this server.",
            BackupSkipReason.MissingChannel =>
                $"{subject} {was} skipped because {its} channel isn't on this server yet. Refresh the IPTV playlist, then restore this section again.",
            BackupSkipReason.MissingIptvPlaylist =>
                $"{subject} {was} skipped because {its} IPTV playlist isn't on this server. Restore IPTV Playlists too.",
            BackupSkipReason.MissingPodcast =>
                $"{subject} {was} skipped because {its} podcast isn't on this server. Restore the Podcast Catalog too.",
            BackupSkipReason.MissingRecordingSchedule =>
                $"{subject} {was} skipped because {its} recording schedule isn't on this server. Restore IPTV Recording Schedules once the playlist's channels have loaded, then restore this section again.",
            BackupSkipReason.MissingFile =>
                $"{subject} {was} skipped because {its} file isn't on this server. Copy the recordings folder over, then restore this section again.",
            BackupSkipReason.Duplicate =>
                $"{subject} {was} dropped because {(one ? "it" : "they")} matched the same item as another row on this server.",
            _ => $"{subject} {was} skipped."
        };
    }
}
