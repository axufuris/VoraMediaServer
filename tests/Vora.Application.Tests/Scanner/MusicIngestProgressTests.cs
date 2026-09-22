using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

// The parse loop reported per file and the write phase reported nothing, so a
// large library sat on "(n/n)" for the whole write phase and looked hung. The
// two phases are separately slow; both have to be visible. A minimal WAV is
// enough for TagLib to parse, which is what gets us through the parse loop and
// into the writes.
public class MusicIngestProgressTests : IDisposable
{
    private sealed class CapturingProgress : ITaskProgressReporter
    {
        public List<string> Reports { get; } = new();
        public void Report(string? detail)
        {
            if (detail != null) Reports.Add(detail);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Errors.Add(formatter(state, exception) + " :: " + exception);
        }
    }

    private readonly CapturingLogger<VoraLocalMediaScannerProvider> _logger = new();
    private readonly string _tempRoot;
    private readonly IMediaIngestionService _ingestion;
    private readonly CapturingProgress _progress = new();
    private readonly VoraLocalMediaScannerProvider _scanner;
    private readonly Guid _libraryId = Guid.NewGuid();

    public MusicIngestProgressTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "vora-music-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _ingestion = Substitute.For<IMediaIngestionService>();
        _scanner = new VoraLocalMediaScannerProvider(_logger, _ingestion, _progress);

        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>(
                (new List<string> { _tempRoot }, null, new List<string>())));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(new HashSet<string>());
        _ingestion.EnsureArtistAsync(
                Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(),
                Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>())
            .Returns(_ => new ArtistHandle(Guid.NewGuid()));
        _ingestion.EnsureAlbumAsync(
                Arg.Any<LibraryHandle>(), Arg.Any<ArtistHandle>(), Arg.Any<string>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(),
                Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>())
            .Returns(_ => new AlbumHandle(Guid.NewGuid()));
        _ingestion.EnsureTrackAsync(
                Arg.Any<LibraryHandle>(), Arg.Any<AlbumHandle>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    // 44-byte canonical PCM header plus a little silence — enough for TagLib to
    // open it and report properties.
    private void WriteWav(string relative, string? album = null, string? artist = null)
    {
        var full = Path.Combine(_tempRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        const int dataBytes = 64;
        using var stream = new FileStream(full, FileMode.Create);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(44100);
        w.Write(44100 * 2 * 2);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        w.Write(new byte[dataBytes]);
        w.Flush();
        stream.Close();

        if (album == null && artist == null) return;

        using var tagFile = TagLib.File.Create(full);
        if (album != null) tagFile.Tag.Album = album;
        if (artist != null) tagFile.Tag.AlbumArtists = new[] { artist };
        tagFile.Save();
    }

    [Fact]
    public async Task Reports_progress_while_writing_not_only_while_parsing()
    {
        WriteWav(Path.Combine("Artist", "Album", "01.wav"));
        WriteWav(Path.Combine("Artist", "Album", "02.wav"));

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        _logger.Errors.Should().BeEmpty();
        _progress.Reports.Should().Contain(r => r.StartsWith("Scanning "), "the parse phase still reports per file");
        _progress.Reports.Should().Contain(r => r.StartsWith("Saving "), "the write phase must report too, or it looks hung");
    }

    [Fact]
    public async Task Write_progress_counts_towards_the_total_file_count()
    {
        WriteWav(Path.Combine("Artist", "Album", "01.wav"));
        WriteWav(Path.Combine("Artist", "Album", "02.wav"));

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        _progress.Reports.Should().Contain(r => r.StartsWith("Saving ") && r.EndsWith("(2/2)"));
    }

    // Albums are grouped by TAG, not by folder — two folders with identical tags
    // are one album — so these carry real tags.
    //
    // Every save re-runs change detection over everything tracked so far, so a
    // library-sized ingest on one DbContext slows down with each album.
    [Fact]
    public async Task Releases_tracked_entities_once_per_album()
    {
        WriteWav(Path.Combine("Artist", "AlbumOne", "01.wav"), album: "AlbumOne", artist: "Artist");
        WriteWav(Path.Combine("Artist", "AlbumTwo", "01.wav"), album: "AlbumTwo", artist: "Artist");

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.Received(2).ReleaseTrackedEntitiesAsync();
    }

    // Edition is Director's Cut / IMAX — a movie and TV concept. Syncing it loads
    // the item with its parts, and that read was paid once per track.
    [Fact]
    public async Task Skips_the_edition_sync_for_tracks()
    {
        WriteWav(Path.Combine("Artist", "Album", "01.wav"));

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.Received(1).AddMediaPartAsync(
            Arg.Any<MediaItemHandle>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), false);
    }
}
