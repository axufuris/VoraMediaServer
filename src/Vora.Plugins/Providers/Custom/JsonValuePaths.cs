using System.Text.Json;

namespace Vora.Plugins.Providers.Custom;

public static class JsonValuePaths
{
    public static IReadOnlyList<string> Values(JsonElement root, string? pathList)
    {
        var values = new List<string>();
        if (string.IsNullOrWhiteSpace(pathList)) return values;

        foreach (var path in pathList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Collect(root, Segments(path), 0, values);
        }

        return values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
    }

    public static string? First(JsonElement root, string? path) => Values(root, path).FirstOrDefault();

    private static string[] Segments(string path)
    {
        var trimmed = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$', '.');
        return trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Collect(JsonElement element, string[] segments, int index, List<string> values)
    {
        if (index == segments.Length)
        {
            Add(element, values);
            return;
        }

        var segment = segments[index];
        var each = segment.EndsWith("[]", StringComparison.Ordinal);
        var name = each ? segment[..^2] : segment;

        var next = name.Length == 0 ? element : Child(element, name);
        if (next.ValueKind == JsonValueKind.Undefined) return;

        if (each)
        {
            if (next.ValueKind != JsonValueKind.Array) return;
            foreach (var item in next.EnumerateArray())
            {
                Collect(item, segments, index + 1, values);
            }
            return;
        }

        Collect(next, segments, index + 1, values);
    }

    private static JsonElement Child(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        return default;
    }

    private static void Add(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                values.Add(element.GetRawText());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Add(item, values);
                }
                break;
        }
    }
}
