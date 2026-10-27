using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Tests.Transcoding;

public sealed class OnDemandSubtitleExtractionTests : IDisposable
{
    private static readonly Guid PartId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TrackId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-on-demand-subs-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogger _log = new();
    private readonly TestLifetime _lifetime = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private FFmpegSubtitleExtractionService NewService() => new(_log, _lifetime);

    private int FfmpegRuns => _log.Messages.Count(message => message.StartsWith("Extracting subtitles from", StringComparison.Ordinal));

    private async Task<SubtitleSource> MovieWithEmbeddedSubtitlesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var srt = Path.Combine(_root, "subs.srt");
        await File.WriteAllTextAsync(srt, "1\n00:00:01,000 --> 00:00:03,000\nPreviously on Vora.\n\n2\n00:00:04,000 --> 00:00:06,000\nIt begins.\n", cancellationToken);
        var movie = Path.Combine(_root, "movie.mkv");
        await FfmpegTool.RunAsync(cancellationToken,
            "-f", "lavfi", "-i", "color=c=black:s=64x64:r=5:d=8",
            "-i", srt,
            "-map", "0:v", "-map", "1:s",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "srt", movie);
        return SubtitleSource.Embedded(movie, 1, 0);
    }

    private static async Task<string> CachedFileAsync(string cachePath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!FFmpegSubtitleExtractionService.IsUsable(cachePath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }
        return cachePath;
    }

    private string ExpectedCachePath(SubtitleSource subtitle) =>
        FFmpegSubtitleExtractionService.CachePath(_root, PartId, TrackId,
            FFmpegSubtitleExtractionService.FingerprintForSource(subtitle.ContentPath, subtitle.StreamIndex) ?? string.Empty);

    [Fact]
    public async Task A_request_that_hangs_up_leaves_the_extraction_to_finish_and_be_cached()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var subtitle = await MovieWithEmbeddedSubtitlesAsync(cancellationToken);
        var service = NewService();
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        var hungUp = () => service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, gone.Token);

        await hungUp.Should().ThrowAsync<OperationCanceledException>();
        var cached = await CachedFileAsync(ExpectedCachePath(subtitle), cancellationToken);
        FFmpegSubtitleExtractionService.IsUsable(cached).Should().BeTrue();
        (await File.ReadAllTextAsync(cached, cancellationToken)).Should().Contain("Previously on Vora.");

        var next = await service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, cancellationToken);

        next.Should().Be(cached);
        FfmpegRuns.Should().Be(1);
    }

    [Fact]
    public async Task A_request_hanging_up_while_ffmpeg_runs_still_delivers_the_file_to_the_next()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var subtitle = await MovieWithEmbeddedSubtitlesAsync(cancellationToken);
        var service = NewService();
        using var leaving = new CancellationTokenSource();

        var first = service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, leaving.Token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (FfmpegRuns == 0 && DateTime.UtcNow < deadline) await Task.Delay(1, cancellationToken);
        await leaving.CancelAsync();
        var second = await service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, cancellationToken);

        second.Should().Be(ExpectedCachePath(subtitle));
        FFmpegSubtitleExtractionService.IsUsable(second ?? string.Empty).Should().BeTrue();
        FfmpegRuns.Should().Be(1);
        try { await first; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Two_waiting_requests_get_the_same_file_from_one_run()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var subtitle = await MovieWithEmbeddedSubtitlesAsync(cancellationToken);
        var service = NewService();

        var results = await Task.WhenAll(
            service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, cancellationToken),
            service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, cancellationToken));

        results.Should().AllBe(ExpectedCachePath(subtitle));
        FfmpegRuns.Should().Be(1);
    }

    [Fact]
    public async Task Server_shutdown_still_stops_an_extraction()
    {
        Assert.SkipUnless(FfmpegTool.IsAvailable(), FfmpegTool.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var subtitle = await MovieWithEmbeddedSubtitlesAsync(cancellationToken);
        var service = NewService();
        _lifetime.StopApplication();

        var produced = await service.GetOrExtractWebVttAsync(subtitle, _root, PartId, TrackId, cancellationToken);

        produced.Should().BeNull();
        FfmpegRuns.Should().Be(0);
        FFmpegSubtitleExtractionService.IsUsable(ExpectedCachePath(subtitle)).Should().BeFalse();
    }

    private sealed class RecordingLogger : ILogger<FFmpegSubtitleExtractionService>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return _messages.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }
}
