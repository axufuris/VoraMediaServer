using System.Diagnostics;
using System.Net;
using Vora.Api.Extensions;

namespace Vora.Api.Tests;

// MusicBrainz allows roughly a request a second and answers a burst with 500s.
// Nothing throttled it, and the resilience handler then retried each failure —
// so a burst became a heavier burst against the thing already refusing.
public class MusicBrainzRateLimitHandlerTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public List<DateTimeOffset> SentAt { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (SentAt) SentAt.Add(DateTimeOffset.UtcNow);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CountingHandler Inner) Build()
    {
        var inner = new CountingHandler();
        var limiter = new MusicBrainzRateLimitHandler { InnerHandler = inner };
        return (new HttpClient(limiter), inner);
    }

    [Fact]
    public async Task Leaves_a_gap_between_consecutive_requests()
    {
        var (client, inner) = Build();

        await client.GetAsync("https://musicbrainz.test/a", TestContext.Current.CancellationToken);
        await client.GetAsync("https://musicbrainz.test/b", TestContext.Current.CancellationToken);

        inner.SentAt.Should().HaveCount(2);
        (inner.SentAt[1] - inner.SentAt[0]).Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }

    // The loop that broke this fired lookups concurrently, so serialising only
    // sequential callers would not have helped.
    [Fact]
    public async Task Serialises_requests_issued_at_the_same_time()
    {
        var (client, inner) = Build();

        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(
            client.GetAsync("https://musicbrainz.test/a", TestContext.Current.CancellationToken),
            client.GetAsync("https://musicbrainz.test/b", TestContext.Current.CancellationToken),
            client.GetAsync("https://musicbrainz.test/c", TestContext.Current.CancellationToken));
        stopwatch.Stop();

        inner.SentAt.Should().HaveCount(3);
        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2),
            "three requests cannot clear a one-per-second gate any faster");
    }

    [Fact]
    public async Task Does_not_delay_the_very_first_request()
    {
        var (client, inner) = Build();

        var stopwatch = Stopwatch.StartNew();
        await client.GetAsync("https://musicbrainz.test/a", TestContext.Current.CancellationToken);
        stopwatch.Stop();

        inner.SentAt.Should().ContainSingle();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_cancelled_caller_does_not_hold_the_gate()
    {
        var (client, inner) = Build();
        await client.GetAsync("https://musicbrainz.test/a", TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelled = async () => await client.GetAsync("https://musicbrainz.test/b", cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        await client.GetAsync("https://musicbrainz.test/c", TestContext.Current.CancellationToken);

        inner.SentAt.Should().HaveCount(2, "the cancelled request never reached the wire, the next one did");
    }
}
