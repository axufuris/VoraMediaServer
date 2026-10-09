using Microsoft.Extensions.Logging.Abstractions;
using Vora.Infrastructure.Transcoding;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class FFmpegAudioTranscodeTests : IDisposable
{
    private static readonly Guid TrackId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime Mtime = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-audio-" + Guid.NewGuid().ToString("N"));

    private static FFmpegAudioTranscodeService NewService() =>
        new(NullLogger<FFmpegAudioTranscodeService>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string WriteSource(string content = "not really a flac")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "song.flac");
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, Mtime);
        return path;
    }

    private string CachedPathFor(string source, int bitrateKbps)
    {
        var info = new FileInfo(source);
        return Path.Combine(FFmpegAudioTranscodeService.CacheDirectory(_root),
            FFmpegAudioTranscodeService.CacheFileName(TrackId, bitrateKbps, "mp3", source, info.Length, info.LastWriteTimeUtc));
    }

    private string WriteCacheFile(string name, int sizeBytes, DateTime lastWriteUtc)
    {
        var directory = FFmpegAudioTranscodeService.CacheDirectory(_root);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public async Task A_cached_transcode_is_served_without_encoding_again()
    {
        var source = WriteSource();
        var cached = CachedPathFor(source, 192);
        Directory.CreateDirectory(Path.GetDirectoryName(cached) ?? _root);
        File.WriteAllText(cached, "mp3 bytes");

        var path = await NewService().GetTranscodedFileAsync(TrackId, source, 192, "mp3", _root, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(cached), path);
        Assert.Equal("mp3 bytes", File.ReadAllText(cached));
    }

    [Fact]
    public async Task A_missing_source_has_no_transcode()
    {
        var path = await NewService().GetTranscodedFileAsync(TrackId, Path.Combine(_root, "gone.flac"), 192, "mp3", _root, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }

    [Fact]
    public void Each_bitrate_and_each_version_of_the_source_gets_its_own_file()
    {
        var name = FFmpegAudioTranscodeService.CacheFileName(TrackId, 192, "mp3", "/music/song.flac", 1000, Mtime);

        Assert.StartsWith($"{TrackId:N}_192k_", name);
        Assert.EndsWith(".mp3", name);
        Assert.NotEqual(name, FFmpegAudioTranscodeService.CacheFileName(TrackId, 128, "mp3", "/music/song.flac", 1000, Mtime));
        Assert.NotEqual(name, FFmpegAudioTranscodeService.CacheFileName(TrackId, 192, "mp3", "/music/song.flac", 1001, Mtime));
        Assert.NotEqual(name, FFmpegAudioTranscodeService.CacheFileName(TrackId, 192, "mp3", "/music/song.flac", 1000, Mtime.AddSeconds(1)));
        Assert.Equal(name, FFmpegAudioTranscodeService.CacheFileName(TrackId, 192, "MP3", "/music/song.flac", 1000, Mtime));
    }

    [Fact]
    public async Task Playing_a_cached_file_keeps_it_from_being_evicted_first()
    {
        var source = WriteSource();
        var cached = CachedPathFor(source, 192);
        var longAgo = DateTime.UtcNow.AddDays(-3);
        WriteCacheFile(Path.GetFileName(cached), 10, longAgo);

        await NewService().GetTranscodedFileAsync(TrackId, source, 192, "mp3", _root, TestContext.Current.CancellationToken);

        Assert.True(File.GetLastWriteTimeUtc(cached) > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task A_file_played_recently_is_not_touched_again()
    {
        var source = WriteSource();
        var cached = CachedPathFor(source, 192);
        var recently = DateTime.UtcNow.AddMinutes(-5);
        WriteCacheFile(Path.GetFileName(cached), 10, recently);

        await NewService().GetTranscodedFileAsync(TrackId, source, 192, "mp3", _root, TestContext.Current.CancellationToken);

        Assert.Equal(recently, File.GetLastWriteTimeUtc(cached), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Pruning_removes_the_least_recently_played_files_until_under_the_cap()
    {
        var now = DateTime.UtcNow;
        var oldest = WriteCacheFile("a.mp3", 100, now.AddDays(-3));
        var older = WriteCacheFile("b.mp3", 100, now.AddDays(-2));
        var newest = WriteCacheFile("c.mp3", 100, now.AddDays(-1));

        FFmpegAudioTranscodeService.PruneCache(FFmpegAudioTranscodeService.CacheDirectory(_root), 150, null, now);

        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void Pruning_never_removes_the_file_just_written()
    {
        var now = DateTime.UtcNow;
        var justWritten = WriteCacheFile("a.mp3", 100, now.AddDays(-3));
        var other = WriteCacheFile("b.mp3", 100, now.AddDays(-2));

        FFmpegAudioTranscodeService.PruneCache(FFmpegAudioTranscodeService.CacheDirectory(_root), 150, justWritten, now);

        Assert.True(File.Exists(justWritten));
        Assert.False(File.Exists(other));
    }

    [Fact]
    public void Pruning_clears_abandoned_partial_files_but_not_ones_being_written()
    {
        var now = DateTime.UtcNow;
        var abandoned = WriteCacheFile("a.mp3.1" + FFmpegAudioTranscodeService.PartialExtension, 100, now - FFmpegAudioTranscodeService.AbandonedAfter - TimeSpan.FromMinutes(1));
        var inProgress = WriteCacheFile("b.mp3.2" + FFmpegAudioTranscodeService.PartialExtension, 100, now);
        var kept = WriteCacheFile("c.mp3", 100, now.AddDays(-1));

        FFmpegAudioTranscodeService.PruneCache(FFmpegAudioTranscodeService.CacheDirectory(_root), 1000, null, now);

        Assert.False(File.Exists(abandoned));
        Assert.True(File.Exists(inProgress));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void Pruning_a_missing_folder_does_nothing()
    {
        FFmpegAudioTranscodeService.PruneCache(Path.Combine(_root, "nothing-here"), 0, null, DateTime.UtcNow);

        Assert.False(Directory.Exists(Path.Combine(_root, "nothing-here")));
    }
}
