using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vora.Domain.Enums;

namespace Vora.Application.Media.Ai;

public sealed record SongProfile(
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Moods,
    TrackEnergy? Energy,
    IReadOnlyList<string> Themes,
    IReadOnlyList<string> GoodFor,
    bool Instrumental)
{
    public const int MaxWords = 4;
    public const int MaxWordLength = 32;

    public static SongProfile Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), null, Array.Empty<string>(), Array.Empty<string>(), false);

    public bool IsEmpty => Genres.Count == 0 && Moods.Count == 0 && Energy == null && Themes.Count == 0 && GoodFor.Count == 0 && !Instrumental;

    public string ToText()
    {
        var parts = new List<string>();
        if (Genres.Count > 0) parts.Add($"Genre: {string.Join(", ", Genres)}");
        AppendFeel(parts, Moods, Energy, Themes, GoodFor, Instrumental);
        return string.Join(". ", parts);
    }

    public static SongProfile Read(JsonElement e) => new(
        Words(e, "genres"),
        Words(e, "moods"),
        ReadEnergy(e),
        Words(e, "themes"),
        Words(e, "goodFor"),
        e.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True);

    public static string Describe(TrackDescriptor track, IReadOnlyList<string> artistTags)
    {
        var parts = new List<string> { $"Song: {track.Title}" };
        if (!string.IsNullOrWhiteSpace(track.Artist)) parts.Add($"Artist: {track.Artist}");
        if (!string.IsNullOrWhiteSpace(track.AlbumTitle)) parts.Add(track.Year is int y ? $"Album: {track.AlbumTitle} ({y})" : $"Album: {track.AlbumTitle}");
        if (!string.IsNullOrWhiteSpace(track.Genre)) parts.Add($"Genre: {track.Genre}");
        if (artistTags.Count > 0) parts.Add($"Tags: {string.Join(", ", artistTags)}");
        AppendFeel(parts, track.Moods ?? new List<string>(), track.Energy, track.Themes ?? new List<string>(), track.GoodFor ?? new List<string>(), track.IsInstrumental == true);
        return string.Join(". ", parts);
    }

    public static string Fingerprint(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void AppendFeel(List<string> parts, IReadOnlyList<string> moods, TrackEnergy? energy, IReadOnlyList<string> themes, IReadOnlyList<string> goodFor, bool instrumental)
    {
        if (moods.Count > 0) parts.Add($"Mood: {string.Join(", ", moods)}");
        if (energy is TrackEnergy level) parts.Add($"Energy: {level.ToString().ToLowerInvariant()}");
        if (themes.Count > 0) parts.Add($"Themes: {string.Join(", ", themes)}");
        if (goodFor.Count > 0) parts.Add($"Good for: {string.Join(", ", goodFor)}");
        if (instrumental) parts.Add("Instrumental");
    }

    internal static List<string> Words(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return new List<string>();
        var raw = v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty),
            JsonValueKind.String => (v.GetString() ?? string.Empty).Split(','),
            _ => Enumerable.Empty<string>()
        };
        return raw
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length is > 0 and <= MaxWordLength)
            .Distinct()
            .Take(MaxWords)
            .ToList();
    }

    internal static TrackEnergy? ReadEnergy(JsonElement e) =>
        e.TryGetProperty("energy", out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "low" => TrackEnergy.Low,
                "medium" => TrackEnergy.Medium,
                "high" => TrackEnergy.High,
                _ => null
            }
            : null;
}
