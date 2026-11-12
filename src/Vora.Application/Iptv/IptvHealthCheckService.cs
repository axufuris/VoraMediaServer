using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Iptv;

public interface IIptvHealthCheckService
{
    Task CheckPlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default);
}

public class IptvHealthCheckService : IIptvHealthCheckService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);
    private const int DefaultParallelism = 4;
    private const int MaxParallelism = 6;

    private readonly IIptvRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIptvUpstreamResolver _resolver;
    private readonly ITaskProgressReporter _progress;
    private readonly ILogger<IptvHealthCheckService> _logger;

    public IptvHealthCheckService(
        IIptvRepository repository,
        IHttpClientFactory httpClientFactory,
        IIptvUpstreamResolver resolver,
        ITaskProgressReporter progress,
        ILogger<IptvHealthCheckService> logger)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _progress = progress;
        _logger = logger;
    }

    public async Task CheckPlaylistAsync(Guid playlistId, CancellationToken cancellationToken = default)
    {
        var playlist = await _repository.GetPlaylistByIdAsync(playlistId);
        if (playlist == null || !playlist.EnableHealthCheck) return;

        var channels = playlist.Channels.ToList();
        if (channels.Count == 0) return;

        List<(Guid Id, IptvUpstream Upstream)> streams;
        try
        {
            streams = channels.Select(c => (c.Id, _resolver.Resolve(playlist, c))).ToList();
        }
        catch (IptvSourceException ex)
        {
            _logger.LogWarning("IPTV health check for '{Playlist}' skipped: {Error}", playlist.Name, ex.Message);
            return;
        }

        // Probe no wider than the playlist's tuner limit so a health check never
        // out-hammers what the admin already deemed safe for that provider. A
        // "0 = unlimited" tuner setting still gets a conservative cap here.
        var cap = IptvConnectionLimit.Of(playlist);
        var parallelism = cap > 0 ? Math.Min(cap, MaxParallelism) : DefaultParallelism;

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        var results = new ConcurrentDictionary<Guid, bool>();
        var total = streams.Count;
        var done = 0;

        await Parallel.ForEachAsync(
            streams,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (stream, ct) =>
            {
                results[stream.Id] = await ProbeAsync(client, stream.Upstream, ct);
                var n = Interlocked.Increment(ref done);
                _progress.Report($"Checking {playlist.Name} ({n}/{total})");
            });

        await _repository.UpdateChannelHealthAsync(results, DateTime.UtcNow);

        var unhealthy = results.Count(r => !r.Value);
        _logger.LogInformation("IPTV health check for '{Playlist}': {Unhealthy} of {Total} channel(s) unreachable.", playlist.Name, unhealthy, total);
    }

    private static async Task<bool> ProbeAsync(HttpClient client, IptvUpstream upstream, CancellationToken cancellationToken)
    {
        var url = upstream.Url;
        // Non-HTTP schemes (rtmp/udp/…) can't be probed over HttpClient — treat
        // as healthy rather than hiding a channel we simply can't test.
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        try
        {
            using var request = upstream.Policy.Get(url);
            // Ask for just the first bytes so we read headers, not the whole live
            // stream. Servers that ignore Range return 200; both count as alive.
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
