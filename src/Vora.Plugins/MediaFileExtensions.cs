namespace Vora.Plugins;

// The single source of truth for which files Vora ingests. Both halves of the
// ingest path read it: the scanner, which parses the file, and the folder
// watcher in Vora.Infrastructure, which decides whether an event is worth
// queueing at all. They used to keep private copies, and the watcher's copy had
// no audio formats in it — so a monitored music library reported itself as
// watched and then silently discarded every event. Add a format here, never
// beside a caller.
public static class MediaFileExtensions
{
    public static readonly IReadOnlyList<string> Video = new[] { ".mkv", ".mp4", ".avi", ".m4v" };

    public static readonly IReadOnlyList<string> Audio = new[] { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aac", ".wma" };

    public static bool IsVideo(string filePath) => Matches(Video, filePath);

    public static bool IsAudio(string filePath) => Matches(Audio, filePath);

    public static bool IsMedia(string filePath) => IsVideo(filePath) || IsAudio(filePath);

    private static bool Matches(IReadOnlyList<string> extensions, string filePath) =>
        extensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
}
