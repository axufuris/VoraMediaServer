using System.Text.Json;

namespace Vora.Application.Ai;

public static class AiModelNames
{
    private static readonly string[] EmbeddingMarkers = { "embed", "bge-", "e5-", "gte-", "minilm" };

    private static readonly string[] OpenAiNonChatMarkers =
    {
        "whisper", "tts", "dall-e", "image", "moderation", "audio", "realtime", "transcribe", "davinci", "babbage", "sora"
    };

    private static readonly string[] ReasoningPrefixes = { "gpt-5", "o1", "o3", "o4" };

    public static bool IsEmbedding(string model) =>
        EmbeddingMarkers.Any(marker => model.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static bool IsChat(string model, bool onOpenAi) =>
        !IsEmbedding(model)
        && !(onOpenAi && OpenAiNonChatMarkers.Any(marker => model.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    public static bool IsReasoning(string model)
    {
        var name = model[(model.LastIndexOf('/') + 1)..];
        return ReasoningPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> Read(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array
                    ? models
                    : default;

        var names = new List<string>();
        if (list.ValueKind != JsonValueKind.Array) return names;

        foreach (var entry in list.EnumerateArray())
        {
            var name = entry.ValueKind == JsonValueKind.String
                ? entry.GetString()
                : entry.ValueKind == JsonValueKind.Object
                    ? StringProperty(entry, "id") ?? StringProperty(entry, "name") ?? StringProperty(entry, "model")
                    : null;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? StringProperty(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
