using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Backups;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests.Backups;

internal sealed class MemoryBackupArchive : IBackupWriter, IBackupReader
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public void Remove(string path) => _files.Remove(path);

    public Task WriteJsonAsync<T>(string path, T payload, CancellationToken ct)
    {
        _files[path] = JsonSerializer.SerializeToUtf8Bytes(payload, BackupJson.Options);
        return Task.CompletedTask;
    }

    public Task WriteBytesAsync(string path, byte[] payload, CancellationToken ct)
    {
        _files[path] = payload;
        return Task.CompletedTask;
    }

    public Task<long> GetSectionSizeAsync(CancellationToken ct) => Task.FromResult(0L);

    public int GetSectionRowCount() => 0;

    public Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(path, out var bytes) ? JsonSerializer.Deserialize<T>(bytes) : default);

    public Task<byte[]?> ReadBytesAsync(string path, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(path, out var bytes) ? bytes : null);

    public void BeginSection(string sectionKey) { }

    public void EndSection() { }
}

internal sealed class BackupTestWorld : IDisposable
{
    public static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ProfileId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid DeviceId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    public BackupTestWorld(VoraDbContext db)
    {
        Db = db;
        References = new BackupReferenceMapper(Db);
    }

    public static VoraDbContext InMemoryContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("backup-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    public VoraDbContext Db { get; }
    public BackupReferenceMapper References { get; }

    public Guid MoviesLibrary { get; private set; }
    public Guid ShowsLibrary { get; private set; }
    public Guid MusicLibrary { get; private set; }
    public Guid Matrix { get; private set; }
    public Guid HomeVideo { get; private set; }
    public Guid Pilot { get; private set; }
    public Guid ParanoidAndroid { get; private set; }
    public Guid OkComputer { get; private set; }
    public Guid Radiohead { get; private set; }
    public Guid OnlyOnSource { get; private set; }

    public static BackupTestWorld Source(VoraDbContext? db = null)
    {
        var world = new BackupTestWorld(db ?? InMemoryContext());
        world.SeedAccount(withDevice: true);
        world.SeedLibrary(includeSourceOnlyMovie: true);
        return world;
    }

    public static BackupTestWorld RebuiltTarget(bool withAccount = true, bool withDevice = false, VoraDbContext? db = null)
    {
        var world = new BackupTestWorld(db ?? InMemoryContext());
        if (withAccount) world.SeedAccount(withDevice);
        world.SeedLibrary(includeSourceOnlyMovie: false);
        return world;
    }

    public void SeedAccount(bool withDevice)
    {
        Db.Users.Add(new User { Id = UserId, Email = "admin@example.com", DisplayName = "Admin" });
        Db.UserProfiles.Add(new UserProfile { Id = ProfileId, Name = "Andy", UserId = UserId });
        if (withDevice) Db.ClientDevices.Add(new ClientDevice { Id = DeviceId, DeviceId = "living-room-tv" });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void SeedLibrary(bool includeSourceOnlyMovie)
    {
        MoviesLibrary = AddLibrary("Movies", LibraryType.Movie);
        ShowsLibrary = AddLibrary("TV Shows", LibraryType.TvShow);
        MusicLibrary = AddLibrary("Music", LibraryType.Music);

        var matrix = new Movie { Title = "The Matrix", TmdbId = "603", ReleaseDate = new DateOnly(1999, 3, 31), LibraryId = MoviesLibrary };
        var homeVideo = new Movie { Title = "Birthday 2019", LibraryId = MoviesLibrary };
        Db.MediaItems.AddRange(matrix, homeVideo);
        Matrix = matrix.Id;
        HomeVideo = homeVideo.Id;

        if (includeSourceOnlyMovie)
        {
            var onlyHere = new Movie { Title = "Gone Tomorrow", TmdbId = "999", LibraryId = MoviesLibrary };
            Db.MediaItems.Add(onlyHere);
            OnlyOnSource = onlyHere.Id;
        }

        var show = new TvShow { Title = "Breaking Bad", TvdbId = "81189", LibraryId = ShowsLibrary };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = ShowsLibrary };
        var pilot = new Episode { Title = "Cat's in the Bag", EpisodeNumber = 2, SeasonId = season.Id, LibraryId = ShowsLibrary };
        Db.MediaItems.AddRange(show, season, pilot);
        Pilot = pilot.Id;

        var artist = new Artist { Name = "Radiohead", MusicBrainzId = "a74b1b7f-71a5-4011-9441-d0b5e4122711", LibraryId = MusicLibrary };
        var album = new Album { Title = "OK Computer", MusicBrainzId = "b1392450-e666-3926-a536-22c65f834433", ArtistId = artist.Id, LibraryId = MusicLibrary };
        var track = new Track { Title = "Paranoid Android", TrackNumber = 2, AlbumId = album.Id, Artist = "Radiohead", LibraryId = MusicLibrary };
        Db.Artists.Add(artist);
        Db.Albums.Add(album);
        Db.MediaItems.Add(track);
        Radiohead = artist.Id;
        OkComputer = album.Id;
        ParanoidAndroid = track.Id;

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    private Guid AddLibrary(string name, LibraryType type)
    {
        var library = new MediaLibrary { Name = name, Type = type, FolderPaths = new List<string> { "/media/" + name } };
        Db.MediaLibraries.Add(library);
        return library.Id;
    }

    public static async Task<(MemoryBackupArchive Archive, BackupSectionImportResult Result)> CopyAsync(
        IBackupSection from,
        IBackupSection to,
        Action<MemoryBackupArchive>? tamper = null)
    {
        var archive = new MemoryBackupArchive();
        await from.WriteAsync(archive, TestContext.Current.CancellationToken);
        tamper?.Invoke(archive);
        var result = await to.ReadAsync(archive, TestContext.Current.CancellationToken);
        return (archive, result);
    }

    public void Dispose() => Db.Dispose();
}
