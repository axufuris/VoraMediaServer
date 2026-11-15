using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Vora.Application.Net;

namespace Vora.Application.Podcasts;

public interface IPodcastStreamResolver
{
    /// <summary>
    /// Where <paramref name="audioUrl"/>'s redirect chain ends: the address of
    /// the file itself, or null when the chain breaks, loops, runs too long or
    /// reaches an address the server may not fetch.
    /// </summary>
    Task<string?> ResolveAsync(string audioUrl, string? userAgent, CancellationToken cancellationToken);
}

// Feeds put an episode's audio behind a chain of tracking redirects. One show's
// goes podtrac -> pscrb.fm -> clrtpod.com -> mgln.ai -> arttrk.com ->
// prfx.byspotify.com -> traffic.libsyn.com -> the file. A browser with a
// tracker blocker breaks that chain part-way: uBlock Origin rewrites several of
// the hops, the media request fails at arttrk.com, and the episode never
// starts. The web player then asks the server where the chain ends and plays
// that address, which is the podcast host's own and isn't blocked.
//
// The chain is walked by hand so every hop is checked like a feed address
// (PodcastAddressRules): a feed is written by whoever may add one, and must not
// be able to walk the server onto its own network. Each hop asks for the first
// byte only, as a player would, and the body is never read.
public class PodcastStreamResolver : IPodcastStreamResolver
{
    public const string HttpClientName = "PodcastStreamResolver";
    public const int MaxRedirects = 15;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PodcastStreamResolver> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _lookUpHost;

    public PodcastStreamResolver(IHttpClientFactory httpClientFactory, ILogger<PodcastStreamResolver> logger)
        : this(httpClientFactory, logger, Dns.GetHostAddressesAsync)
    {
    }

    internal PodcastStreamResolver(
        IHttpClientFactory httpClientFactory,
        ILogger<PodcastStreamResolver> logger,
        Func<string, CancellationToken, Task<IPAddress[]>> lookUpHost)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _lookUpHost = lookUpHost;
    }

    public async Task<string?> ResolveAsync(string audioUrl, string? userAgent, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var current)) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!await MayFetchAsync(current, cts.Token))
                {
                    _logger.LogWarning("Podcast audio not resolved: hop {Hop} goes to {Host}, which the server may not fetch", hop, current.Host);
                    return null;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Range = new RangeHeaderValue(0, 0);
                if (!string.IsNullOrWhiteSpace(userAgent)) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (SafeImageDownloader.IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (response.IsSuccessStatusCode) return current.AbsoluteUri;

                _logger.LogWarning("Podcast audio not resolved: {Host} answered {Status} after {Hops} redirects", current.Host, (int)response.StatusCode, hop);
                return null;
            }

            _logger.LogWarning("Podcast audio not resolved: more than {MaxRedirects} redirects", MaxRedirects);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("Podcast audio not resolved: {Host} could not be reached ({Error})", current.Host, ex.Message);
            return null;
        }
    }

    private async Task<bool> MayFetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _lookUpHost(uri.DnsSafeHost, cancellationToken);
            }
            catch (System.Net.Sockets.SocketException)
            {
                return false;
            }
        }

        return addresses.Length > 0 && !addresses.Any(PodcastAddressRules.IsDisallowedAddress);
    }
}
