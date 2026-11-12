using System.Net;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Iptv;

public sealed record IptvChannelLoad(List<IptvChannel> Channels, XtreamAccountInfo? Account);

public interface IIptvChannelLoader
{
    Task<IptvChannelLoad> LoadAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default);
}

public class IptvChannelLoader : IIptvChannelLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIptvUpstreamResolver _resolver;

    public IptvChannelLoader(IHttpClientFactory httpClientFactory, IIptvUpstreamResolver resolver)
    {
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
    }

    public async Task<IptvChannelLoad> LoadAsync(IptvPlaylist playlist, CancellationToken cancellationToken = default)
    {
        if (playlist.SourceType == IptvSourceType.Xtream)
        {
            return await LoadXtreamAsync(playlist, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(playlist.M3uUrl))
        {
            throw new IptvSourceException("The playlist has no M3U address.");
        }

        var policy = _resolver.PolicyFor(playlist);
        if (IsRadioBrowserUrl(playlist.M3uUrl))
        {
            var json = await GetStringAsync(ConvertToRadioBrowserJsonUrl(playlist.M3uUrl), policy, cancellationToken);
            return new IptvChannelLoad(RadioBrowserParser.Parse(json, playlist.Id, playlist.DefaultChannelKind), null);
        }

        var content = await GetStringAsync(playlist.M3uUrl, policy, cancellationToken);
        return new IptvChannelLoad(M3uChannelParser.Parse(content, playlist.Id, playlist.DefaultChannelKind), null);
    }

    private async Task<IptvChannelLoad> LoadXtreamAsync(IptvPlaylist playlist, CancellationToken cancellationToken)
    {
        var account = _resolver.XtreamAccountFor(playlist);
        var policy = _resolver.PolicyFor(playlist);

        var info = XtreamParser.ParseAccount(await GetStringAsync(account.PlayerApiUrl(), policy, cancellationToken));
        if (XtreamParser.AccountProblem(info, DateTime.UtcNow) is { } problem)
        {
            throw new XtreamAccountException(problem, info);
        }

        var categories = XtreamParser.ParseCategories(await GetStringAsync(account.PlayerApiUrl("get_live_categories"), policy, cancellationToken));
        var streams = await GetStringAsync(account.PlayerApiUrl("get_live_streams"), policy, cancellationToken);
        var extension = XtreamParser.PreferredExtension(info.AllowedOutputFormats);
        var channels = XtreamParser.ParseLiveStreams(streams, playlist.Id, playlist.DefaultChannelKind, categories, extension);
        return new IptvChannelLoad(channels, info);
    }

    private async Task<string> GetStringAsync(string url, IptvRequestPolicy policy, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        using var request = policy.Get(url);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new IptvSourceException(StatusMessage(response.StatusCode));
            }
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IptvSourceException($"The provider could not be reached: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IptvSourceException("The provider did not answer in time.", ex);
        }
    }

    private static string StatusMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"The provider refused access (HTTP {(int)status}). Check the username, password and headers.",
        HttpStatusCode.NotFound => "The provider answered HTTP 404. Check the address.",
        _ => $"The provider answered HTTP {(int)status}."
    };

    private static bool IsRadioBrowserUrl(string url) =>
        url.Contains("radio-browser.info", StringComparison.OrdinalIgnoreCase);

    private static string ConvertToRadioBrowserJsonUrl(string url) =>
        url.Replace("/m3u/", "/json/", StringComparison.OrdinalIgnoreCase);
}

public class XtreamAuthenticationException : IptvSourceException
{
    public XtreamAuthenticationException(string message)
        : base(message)
    {
    }
}

public class XtreamAccountException : IptvSourceException
{
    public XtreamAccountException(string message, XtreamAccountInfo account)
        : base(message)
    {
        Account = account;
    }

    public XtreamAccountInfo Account { get; }
}
