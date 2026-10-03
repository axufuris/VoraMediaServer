namespace Vora.Application.Media;

public static class SongMoods
{
    public const int MinTracksToBrowse = 5;

    public static IReadOnlyList<string> All { get; } = new[]
    {
        "happy", "upbeat", "euphoric", "playful", "uplifting", "confident",
        "romantic", "sensual", "groovy", "chill", "calm", "dreamy",
        "nostalgic", "bittersweet", "melancholy", "sad",
        "dark", "mysterious", "intense", "aggressive", "rebellious", "epic"
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Variants = new(StringComparer.Ordinal)
    {
        ["joyful"] = "happy",
        ["cheerful"] = "happy",
        ["feel-good"] = "happy",
        ["feel good"] = "happy",
        ["energetic"] = "upbeat",
        ["lively"] = "upbeat",
        ["bouncy"] = "upbeat",
        ["ecstatic"] = "euphoric",
        ["fun"] = "playful",
        ["quirky"] = "playful",
        ["whimsical"] = "playful",
        ["hopeful"] = "uplifting",
        ["inspiring"] = "uplifting",
        ["inspirational"] = "uplifting",
        ["empowering"] = "confident",
        ["swagger"] = "confident",
        ["cocky"] = "confident",
        ["tender"] = "romantic",
        ["loving"] = "romantic",
        ["passionate"] = "romantic",
        ["sexy"] = "sensual",
        ["seductive"] = "sensual",
        ["sultry"] = "sensual",
        ["funky"] = "groovy",
        ["relaxed"] = "chill",
        ["relaxing"] = "chill",
        ["laid-back"] = "chill",
        ["laid back"] = "chill",
        ["laidback"] = "chill",
        ["mellow"] = "chill",
        ["chilled"] = "chill",
        ["peaceful"] = "calm",
        ["serene"] = "calm",
        ["tranquil"] = "calm",
        ["soothing"] = "calm",
        ["ethereal"] = "dreamy",
        ["hazy"] = "dreamy",
        ["wistful"] = "nostalgic",
        ["sentimental"] = "nostalgic",
        ["melancholic"] = "melancholy",
        ["somber"] = "melancholy",
        ["sombre"] = "melancholy",
        ["sorrowful"] = "sad",
        ["heartbroken"] = "sad",
        ["mournful"] = "sad",
        ["brooding"] = "dark",
        ["ominous"] = "dark",
        ["gloomy"] = "dark",
        ["sinister"] = "dark",
        ["haunting"] = "mysterious",
        ["eerie"] = "mysterious",
        ["enigmatic"] = "mysterious",
        ["dramatic"] = "intense",
        ["tense"] = "intense",
        ["angry"] = "aggressive",
        ["furious"] = "aggressive",
        ["fierce"] = "aggressive",
        ["defiant"] = "rebellious",
        ["triumphant"] = "epic",
        ["anthemic"] = "epic",
        ["cinematic"] = "epic"
    };

    public static bool IsMood(string? word) => word != null && Known.Contains(word.Trim().ToLowerInvariant());

    public static string? Normalize(string? word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        var key = word.Trim().ToLowerInvariant();
        if (Known.Contains(key)) return key;
        return Variants.TryGetValue(key, out var mood) ? mood : null;
    }

    public static List<string> Normalize(IEnumerable<string> words) =>
        words.Select(Normalize).OfType<string>().Distinct().ToList();

    public static string DisplayName(string mood) =>
        mood.Length == 0 ? mood : char.ToUpperInvariant(mood[0]) + mood[1..];
}
