using System.Text.RegularExpressions;

namespace Vora.Application.Iptv;

public sealed class XtreamAccount
{
    private static readonly string[] PanelFiles = { "player_api.php", "get.php", "xmltv.php", "panel_api.php", "c" };

    public XtreamAccount(string serverUrl, string username, string password)
    {
        ServerUrl = serverUrl;
        Username = username;
        Password = password;
    }

    public string ServerUrl { get; }
    public string Username { get; }
    public string Password { get; }

    public static string? NormalizeServerUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && PanelFiles.Contains(segments[^1], StringComparer.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }

        var path = segments.Count == 0 ? string.Empty : "/" + string.Join('/', segments);
        return $"{uri.Scheme}://{uri.Authority}{path}";
    }

    public string PlayerApiUrl(string? action = null)
    {
        var url = $"{ServerUrl}/player_api.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}";
        return action is null ? url : $"{url}&action={Uri.EscapeDataString(action)}";
    }

    public string XmlTvUrl() =>
        $"{ServerUrl}/xmltv.php?username={Uri.EscapeDataString(Username)}&password={Uri.EscapeDataString(Password)}";

    public string LiveStreamUrl(string streamId, string extension) =>
        $"{ServerUrl}/live/{Uri.EscapeDataString(Username)}/{Uri.EscapeDataString(Password)}/{streamId}.{extension}";
}

public static class XtreamStreamReference
{
    public const string Scheme = "xtream:";

    private static readonly Regex Reference = new(@"^xtream:(?<id>[A-Za-z0-9_\-]+)\.(?<ext>[A-Za-z0-9]+)$", RegexOptions.Compiled);

    public static string For(string streamId, string extension) => $"{Scheme}{streamId}.{extension}";

    public static bool IsReference(string? streamUrl) =>
        streamUrl is not null && streamUrl.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? streamUrl, out string streamId, out string extension)
    {
        streamId = string.Empty;
        extension = string.Empty;
        if (streamUrl is null) return false;

        var match = Reference.Match(streamUrl);
        if (!match.Success) return false;

        streamId = match.Groups["id"].Value;
        extension = match.Groups["ext"].Value;
        return true;
    }
}
