namespace Vora.Api.Extensions;

// MusicBrainz allows roughly one request per second per IP and answers a burst
// with 500s. Nothing throttled it: artwork lookups ran in a tight loop, and the
// standard resilience handler then RETRIED each failure, so a burst turned into
// a heavier burst against the thing that was already refusing.
//
// Two callers share this client — the MusicBrainz cover-art provider, and the
// Fanart.tv provider, which resolves an artist's MBID through MusicBrainz before
// it can ask Fanart for anything. So a rate-limited MusicBrainz takes Fanart.tv
// artwork down with it, which is why artist backgrounds and banners came back
// empty while TheAudioDB images arrived fine.
//
// One request at a time, with a minimum gap between them. Slow by design: the
// work behind it is a background refresh, and being refused is slower still.
public sealed class MusicBrainzRateLimitHandler : DelegatingHandler
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var sinceLast = DateTimeOffset.UtcNow - _lastRequestAt;
            if (sinceLast < MinimumInterval)
            {
                await Task.Delay(MinimumInterval - sinceLast, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            // Stamped on the way out, so the gap is measured between the end of
            // one request and the start of the next. Measuring from the start
            // would let a slow response be followed immediately by another.
            _lastRequestAt = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }
}
