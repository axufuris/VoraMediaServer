using System.Text.RegularExpressions;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public sealed record SongFacts(
    string? Artist,
    string? Title,
    int? DurationSeconds,
    string? AudioCodec = null,
    int? SampleRate = null,
    int? Bitrate = null);

public sealed class SongCopies<T>
{
    private readonly List<T> _all;
    private (int Lossless, int Detail, int Bitrate) _bestRank;

    internal SongCopies(T first, SongFacts facts)
    {
        Best = first;
        _all = new List<T> { first };
        DurationSeconds = facts.DurationSeconds;
        _bestRank = SameSong.Rank(facts);
    }

    public T Best { get; private set; }
    public IReadOnlyList<T> All => _all;
    internal int? DurationSeconds { get; }

    internal void Add(T copy, SongFacts facts)
    {
        _all.Add(copy);
        var rank = SameSong.Rank(facts);
        if (rank.CompareTo(_bestRank) <= 0) return;
        Best = copy;
        _bestRank = rank;
    }
}

public static partial class SameSong
{
    public const int DurationToleranceSeconds = 5;

    public static List<SongCopies<T>> Group<T>(IEnumerable<T> items, Func<T, SongFacts> factsOf)
    {
        var songs = new List<SongCopies<T>>();
        var byName = new Dictionary<string, List<SongCopies<T>>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var facts = factsOf(item);
            var key = Key(facts);

            if (key != null && byName.TryGetValue(key, out var named))
            {
                var same = named.FirstOrDefault(s => CloseInLength(s.DurationSeconds, facts.DurationSeconds));
                if (same != null)
                {
                    same.Add(item, facts);
                    continue;
                }
            }

            var song = new SongCopies<T>(item, facts);
            songs.Add(song);
            if (key == null) continue;
            if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<SongCopies<T>>();
            list.Add(song);
        }

        return songs;
    }

    public static bool Same(SongFacts a, SongFacts b) =>
        Key(a) is string key && key == Key(b) && CloseInLength(a.DurationSeconds, b.DurationSeconds);

    internal static string? Key(SongFacts facts)
    {
        var artist = string.IsNullOrWhiteSpace(facts.Artist) ? string.Empty : MusicNameKey.Normalize(Featuring().Replace(facts.Artist, string.Empty));
        var title = string.IsNullOrWhiteSpace(facts.Title)
            ? string.Empty
            : MusicEditionMatcher.TrackKey(EditionLabel().Replace(Featuring().Replace(facts.Title, string.Empty), string.Empty));
        return artist.Length == 0 || title.Length == 0 ? null : $"{artist}\u001f{title}";
    }

    internal static (int Lossless, int Detail, int Bitrate) Rank(SongFacts facts)
    {
        var quality = AudioQuality.For(facts.AudioCodec, facts.SampleRate, facts.Bitrate);
        var bitrate = facts.Bitrate ?? 0;
        return quality is { Lossless: true }
            ? (1, quality.SampleRate ?? 0, bitrate)
            : (0, bitrate, 0);
    }

    private static bool CloseInLength(int? a, int? b) =>
        a is not int x || b is not int y || Math.Abs(x - y) <= DurationToleranceSeconds;

    [GeneratedRegex(@"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]|\s+(?:feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s*[\(\[]\s*(?:[^\)\]]*\bremaster(?:ed)?\b[^\)\]]*|(?:album|single|lp)\s+version)\s*[\)\]]|\s+-\s+(?:[^-]*\bremaster(?:ed)?\b.*|(?:album|single|lp)\s+version)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EditionLabel();
}
