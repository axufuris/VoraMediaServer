using System.Text.RegularExpressions;

namespace Vora.Application.Ai;

public static partial class AiJson
{
    public static string Extract(string content)
    {
        var text = ThinkingBlock().Replace(content, string.Empty).Trim();
        if (text.Length == 0 || text[0] == '[') return text;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    [GeneratedRegex(@"<think(ing)?>.*?</think(ing)?>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkingBlock();
}
