using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

public class MusicLibraryRepairTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-music-identity-" + Guid.NewGuid().ToString("N"));
    private readonly IMediaIngestionService _ingestion = Substitute.For<IMediaIngestionService>();
    private readonly VoraLocalMediaScannerProvider _scanner;
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly List<(string Title, IReadOnlyList<string> Folders)> _albumsEnsured = [];
    private HashSet<string> _known = [];

    public MusicLibraryRepairTests()
    {
        Directory.CreateDirectory(_root);
        _scanner = new VoraLocalMediaScannerProvider(NullLogger<VoraLocalMediaScannerProvider>.Instance, _ingestion, new NullTaskProgressReporter());
        _ingestion.GetLibraryDetailsAsync(Arg.Any<LibraryHandle>())
            .Returns(Task.FromResult<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)>(([_root], null, [])));
        _ingestion.GetExistingLibraryPathsAsync(Arg.Any<LibraryHandle>()).Returns(_ => _known);
        _ingestion.GetMusicFilePlacementsAsync(Arg.Any<LibraryHandle>()).Returns([]);
        _ingestion.EnsureArtistAsync(Arg.Any<LibraryHandle>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(),
                Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>())
            .Returns(_ => new ArtistHandle(Guid.NewGuid()));
        _ingestion.EnsureAlbumAsync(Arg.Any<LibraryHandle>(), Arg.Any<ArtistHandle>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(),
                Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(), Arg.Any<byte[]?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(call =>
            {
                _albumsEnsured.Add(((string)call[2], (IReadOnlyList<string>?)call[13] ?? []));
                return new AlbumHandle(Guid.NewGuid());
            });
        _ingestion.EnsureTrackAsync(Arg.Any<LibraryHandle>(), Arg.Any<AlbumHandle>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(_ => new MediaItemHandle(Guid.NewGuid()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Song(string folder, string fileName, string title, string album = "Weezer", uint track = 1, uint disc = 0)
    {
        var full = Path.Combine(_root, "Weezer", folder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? _root);
        const int dataBytes = 64;
        using (var stream = new FileStream(full, FileMode.Create))
        using (var w = new BinaryWriter(stream))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray()); w.Write("fmt "u8.ToArray());
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(44100); w.Write(44100 * 4); w.Write((short)4); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(dataBytes); w.Write(new byte[dataBytes]);
        }
        using var tagFile = TagLib.File.Create(full);
        tagFile.Tag.Title = title;
        tagFile.Tag.Album = album;
        tagFile.Tag.AlbumArtists = ["Weezer"];
        tagFile.Tag.Track = track;
        tagFile.Tag.Disc = disc;
        tagFile.Save();
        return full;
    }

    private string Folder(string name) => Path.Combine(_root, "Weezer", name);

    [Fact]
    public async Task Albums_with_the_same_title_in_their_own_folders_stay_apart()
    {
        Song("Weezer (Blue Album)", "01.wav", "My Name Is Jonas", disc: 1);
        Song("Weezer (Teal Album)", "01.wav", "Africa", disc: 1);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        _albumsEnsured.Should().HaveCount(2);
        _albumsEnsured.Select(a => a.Folders.Single()).Should().BeEquivalentTo(Folder("Weezer (Blue Album)"), Folder("Weezer (Teal Album)"));
    }

    [Fact]
    public async Task Disc_folders_make_one_album()
    {
        Song(Path.Combine("Pinkerton", "CD1"), "01.wav", "Tired of Sex", album: "Pinkerton", disc: 1);
        Song(Path.Combine("Pinkerton", "CD2"), "01.wav", "Why Bother? (live)", album: "Pinkerton", disc: 2);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        _albumsEnsured.Should().ContainSingle().Which.Folders.Should().Equal(Folder("Pinkerton"));
    }

    private static MusicFilePlacement On(string file, Guid album, Guid track, string title) => new(file, album, track, title, title, 1);

    [Fact]
    public async Task A_song_stacked_on_another_songs_track_is_taken_off_and_added_again()
    {
        var album = Guid.NewGuid();
        var circus = Guid.NewGuid();
        var own = Song("Circus", "01.wav", "Circus", album: "Circus");
        var acoustic = Song("Circus", "01b.wav", "Circus (acoustic version)", album: "Circus");
        var flac = Song("Circus", "01c.wav", "Circus", album: "Circus");
        _known = [own, acoustic, flac];
        _ingestion.GetMusicFilePlacementsAsync(Arg.Any<LibraryHandle>()).Returns([On(own, album, circus, "Circus"), On(acoustic, album, circus, "Circus"), On(flac, album, circus, "Circus")]);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(acoustic);
        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(own);
        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(flac);
        await _ingestion.Received(1).EnsureTrackAsync(Arg.Any<LibraryHandle>(), Arg.Any<AlbumHandle>(), "Circus (acoustic version)", Arg.Any<int>(), Arg.Any<int?>(),
            Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task An_album_merged_from_several_folders_keeps_one_and_gives_the_others_back()
    {
        var album = Guid.NewGuid();
        var blue1 = Song("Weezer (Blue Album)", "11.wav", "Jamie", track: 11, disc: 1);
        var blue2 = Song("Weezer (Blue Album)", "12.wav", "Mykel and Carli", track: 12, disc: 1);
        var teal = Song("Weezer (Teal Album)", "11.wav", "Rosanna", track: 11, disc: 1);
        _known = [blue1, blue2, teal];
        _ingestion.GetMusicFilePlacementsAsync(Arg.Any<LibraryHandle>()).Returns(
            [On(blue1, album, Guid.NewGuid(), "Jamie"), On(blue2, album, Guid.NewGuid(), "Mykel and Carli"), On(teal, album, Guid.NewGuid(), "Rosanna")]);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(teal);
        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(blue1);
        await _ingestion.DidNotReceive().ForgetAlbumArtAndYearAsync(Arg.Any<Guid>());
        _albumsEnsured.Should().ContainSingle().Which.Folders.Should().Equal(Folder("Weezer (Teal Album)"));
    }

    [Fact]
    public async Task A_kept_folder_whose_own_songs_come_back_gets_its_cover_and_year_again()
    {
        var album = Guid.NewGuid();
        var africa = Guid.NewGuid();
        var tealOwn = Song("Weezer (Teal Album)", "01.wav", "Africa", disc: 1);
        var blueStacked = Song("Weezer (Blue Album)", "01.wav", "My Name Is Jonas", disc: 1);
        var blue11 = Song("Weezer (Blue Album)", "11.wav", "Jamie", track: 11, disc: 1);
        var blue12 = Song("Weezer (Blue Album)", "12.wav", "Mykel and Carli", track: 12, disc: 1);
        _known = [tealOwn, blueStacked, blue11, blue12];
        _ingestion.GetMusicFilePlacementsAsync(Arg.Any<LibraryHandle>()).Returns(
        [
            On(tealOwn, album, africa, "Africa"), On(blueStacked, album, africa, "Africa"),
            On(blue11, album, Guid.NewGuid(), "Jamie"), On(blue12, album, Guid.NewGuid(), "Mykel and Carli"),
        ]);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.Received(1).RemoveMediaItemByPathAsync(blueStacked);
        await _ingestion.Received(1).RemoveMediaItemByPathAsync(tealOwn);
        await _ingestion.Received(1).ForgetAlbumArtAndYearAsync(album);
    }

    [Fact]
    public async Task A_stack_where_no_file_carries_the_tracks_title_is_left_alone()
    {
        var album = Guid.NewGuid();
        var track = Guid.NewGuid();
        var one = Song("Mixed", "01.wav", "First Song", album: "Mixed");
        var two = Song("Mixed", "01b.wav", "Second Song", album: "Mixed");
        _known = [one, two];
        _ingestion.GetMusicFilePlacementsAsync(Arg.Any<LibraryHandle>()).Returns([On(one, album, track, "Renamed By Hand"), On(two, album, track, "Renamed By Hand")]);

        await _scanner.ScanMusicLibraryAsync(_libraryId);

        await _ingestion.DidNotReceive().RemoveMediaItemByPathAsync(Arg.Any<string>());
    }
}
