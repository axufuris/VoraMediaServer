using System.Net.Http.Headers;
using System.Text;

namespace Vora.Application.Iptv;

public sealed class IptvBasicCredentials
{
    public IptvBasicCredentials(string host, int port, string username, string password)
    {
        Host = host;
        Port = port;
        Username = username;
        Password = password;
    }

    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    public string Password { get; }

    public static IptvBasicCredentials? ForUrl(string? url, string? username, string? password)
    {
        if (string.IsNullOrEmpty(username)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
        return new IptvBasicCredentials(uri.Host, uri.Port, username, password ?? string.Empty);
    }

    public bool Covers(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == Port;

    public string HeaderParameter => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
}

public sealed class IptvRequestPolicy
{
    public const int MaxUserAgentLength = 512;
    public const int MaxRefererLength = 1024;

    public static readonly IptvRequestPolicy None = new(null, null, null);

    public IptvRequestPolicy(string? userAgent, string? referer, IptvBasicCredentials? basic)
    {
        UserAgent = CleanHeaderValue(userAgent, MaxUserAgentLength);
        Referer = CleanHeaderValue(referer, MaxRefererLength);
        Basic = basic;
    }

    public string? UserAgent { get; }
    public string? Referer { get; }
    public IptvBasicCredentials? Basic { get; }

    public static string? CleanHeaderValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) return null;
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    public HttpRequestMessage Get(string url, string? fallbackUserAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        Apply(request, fallbackUserAgent);
        return request;
    }

    public void Apply(HttpRequestMessage request, string? fallbackUserAgent = null)
    {
        var userAgent = UserAgent ?? fallbackUserAgent;
        if (userAgent is not null)
        {
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        if (Referer is not null)
        {
            request.Headers.Remove("Referer");
            request.Headers.TryAddWithoutValidation("Referer", Referer);
        }

        if (Basic is not null && request.RequestUri is { IsAbsoluteUri: true } uri && Basic.Covers(uri))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Basic.HeaderParameter);
        }
    }

    public IReadOnlyList<string> FfmpegInputOptions()
    {
        var options = new List<string>();
        if (UserAgent is not null)
        {
            options.Add("-user_agent");
            options.Add(UserAgent);
        }

        if (Referer is not null)
        {
            options.Add("-headers");
            options.Add($"Referer: {Referer}\r\n");
        }

        return options;
    }

    public string FfmpegInputUrl(string url)
    {
        if (Basic is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Basic.Covers(uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return url;
        }

        var rest = uri.GetComponents(UriComponents.Host | UriComponents.Port | UriComponents.PathAndQuery | UriComponents.Fragment, UriFormat.UriEscaped);
        return $"{uri.Scheme}://{Uri.EscapeDataString(Basic.Username)}:{Uri.EscapeDataString(Basic.Password)}@{rest}";
    }
}
