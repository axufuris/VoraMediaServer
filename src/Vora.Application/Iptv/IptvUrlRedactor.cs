using System.Text.RegularExpressions;

namespace Vora.Application.Iptv;

public static class IptvUrlRedactor
{
    public const string Mask = "***";

    private const int ShortestMaskedSecret = 3;

    private const string CredentialKeys =
        "username|user|usr|login|password|pass|passwd|pwd|token|access_token|auth|auth_token|authtoken|apikey|api_key|api-key|key|secret|signature|sig|hdnts|hdnea|wmsauthsign|md5";

    private static readonly Regex UserInfo = new(
        @"(?<scheme>\b[a-z][a-z0-9+.\-]*://)[^/\s?#@'""<>]+@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CredentialQuery = new(
        @"(?<name>[?&;](?:" + CredentialKeys + @")=)[^&#\s'""<>]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XtreamPathSegments = new(
        @"(?<kind>/(?:live|movie|series|timeshift)/)[^/\s?#'""<>]+/[^/\s?#'""<>]+/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XtreamTokenPathSegments = new(
        @"(?<kind>/hlsr/)[^/\s?#'""<>]+/[^/\s?#'""<>]+/[^/\s?#'""<>]+/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XtreamStreamPath = new(
        @"/(?:live|movie|series)/[^/]+/[^/]+/\d+(?:\.[a-z0-9]+)?$|/timeshift/[^/]+/[^/]+/\d+/|/hlsr/[^/]+/[^/]+/[^/]+/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QueryCredential = new(
        @"(?:^|[?&;])(?:" + CredentialKeys + @")=[^&#]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var redacted = UserInfo.Replace(text, m => m.Groups["scheme"].Value + Mask + "@");
        redacted = CredentialQuery.Replace(redacted, m => m.Groups["name"].Value + Mask);
        redacted = XtreamPathSegments.Replace(redacted, m => m.Groups["kind"].Value + Mask + "/" + Mask + "/");
        redacted = XtreamTokenPathSegments.Replace(redacted, m => m.Groups["kind"].Value + Mask + "/" + Mask + "/" + Mask + "/");

        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < ShortestMaskedSecret) continue;
            redacted = redacted.Replace(secret, Mask, StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (!string.Equals(escaped, secret, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(escaped, Mask, StringComparison.Ordinal);
            }
        }

        return redacted;
    }

    public static bool ContainsCredentials(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return UserInfo.IsMatch(url) || CredentialQuery.IsMatch(url);
        }

        return !string.IsNullOrEmpty(uri.UserInfo)
            || QueryCredential.IsMatch(uri.Query)
            || XtreamStreamPath.IsMatch(uri.AbsolutePath);
    }

    public static string? SchemeAndHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        return $"{uri.Scheme}://{uri.Host}";
    }
}
