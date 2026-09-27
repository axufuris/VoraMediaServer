using System.Text.RegularExpressions;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

// Decides a file's advisory from a provider's editions of its album when the
// file has no ISRC to identify its recording exactly.
//
// An album title is shared by its explicit and clean editions — Deezer lists
// both "The Eminem Show"s, with durations within a second of each other — so
// nothing about an untagged file says which one it is. The answer is the
// strictest one across every edition the track matches: a track is Clean only
// when every matching edition says so. A clean file can therefore come out
// Explicit, which hides a song a child could have heard; the reverse would play
// them one they should not, and is not allowed to happen.
public static partial class MusicEditionMatcher
{
    // Enough to absorb encoder padding and a provider rounding to whole seconds,
    // well short of the difference between a song and its extended or live cut.
    public const int DurationToleranceSeconds = 3;

    public static IReadOnlyList<ProviderAlbumEdition> EditionsOf(string artistName, string albumTitle, IEnumerable<ProviderAlbumEdition> editions)
    {
        var artist = MusicNameKey.Normalize(artistName);
        var album = AlbumKey(albumTitle);
        return editions
            .Where(e => MusicNameKey.Normalize(e.ArtistName) == artist && AlbumKey(e.Title) == album)
            .ToList();
    }

    public static ProviderAdvisory Resolve(string trackTitle, int? durationSeconds, IEnumerable<ProviderAlbumEdition> editions)
    {
        var key = TrackKey(trackTitle);
        if (key.Length == 0) return ProviderAdvisory.Unknown;

        var matches = editions
            .SelectMany(e => e.Tracks)
            .Where(t => TrackKey(t.Title) == key && DurationAgrees(durationSeconds, t.DurationSeconds))
            .Select(t => t.Advisory)
            .ToList();

        if (matches.Count == 0) return ProviderAdvisory.Unknown;
        if (matches.Contains(ProviderAdvisory.Explicit)) return ProviderAdvisory.Explicit;
        return matches.All(a => a == ProviderAdvisory.Clean) ? ProviderAdvisory.Clean : ProviderAdvisory.Unknown;
    }

    // Edition labels are dropped from a track title so a clean edition's
    // "Without Me (Clean)" is recognised as the same song, and its answer counted.
    // Other brackets stay: "(Piano Version)" is a different recording.
    internal static string TrackKey(string title) =>
        MusicNameKey.Normalize(AdvisoryLabel().Replace(title, string.Empty));

    // An album's bracketed suffixes are all edition labels — "(Expanded
    // Edition)", "[Explicit]", "(Remastered)" — naming the same songs. Folding
    // them in means more editions are consulted, which can only make the answer
    // stricter; a live or otherwise different cut is still told apart per track
    // by its duration.
    internal static string AlbumKey(string title) =>
        MusicNameKey.Normalize(TrailingBrackets().Replace(title, string.Empty));

    private static bool DurationAgrees(int? local, int? provider) =>
        local is not int a || provider is not int b || Math.Abs(a - b) <= DurationToleranceSeconds;

    [GeneratedRegex(@"\s*[\(\[]\s*(explicit|clean|edited|censored|dirty)(\s+(version|edit))?\s*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex AdvisoryLabel();

    [GeneratedRegex(@"(\s*[\(\[][^\)\]]*[\)\]])+\s*$")]
    private static partial Regex TrailingBrackets();
}
