using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// A "Because you played" tile shows the track's ALBUM cover. Returning every
// pick from one album before moving to the next therefore drew the same image
// two or three times in a row, which reads as the row having duplicated an entry
// rather than as several songs from one record.
public class TopTracksByArtistInterleaveTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("top-tracks-" + Guid.NewGuid().ToString("N"))
            .Options);

        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = _libraryId,
            Name = "Music",
            Type = LibraryType.Music,
            FolderPaths = new List<string> { "/music" }
        });

        db.Set<Artist>().Add(new Artist { Id = _artistId, Name = "311", LibraryId = _libraryId });
        return db;
    }

    private void AddAlbum(VoraDbContext db, string title, int trackCount)
    {
        var albumId = Guid.NewGuid();
        db.Set<Album>().Add(new Album { Id = albumId, Title = title, ArtistId = _artistId, LibraryId = _libraryId });

        for (var i = 1; i <= trackCount; i++)
        {
            db.Set<Track>().Add(new Track
            {
                Id = Guid.NewGuid(),
                Title = $"{title} {i}",
                AlbumId = albumId,
                TrackNumber = i,
                LibraryId = _libraryId
            });
        }
    }

    private static MusicAccessFilter AllAccess => new() { HasAllLibraryAccess = true };

    private static List<string> AlbumOf(IEnumerable<Track> tracks) =>
        tracks.Select(t => t.Title.Split(' ')[0]).ToList();

    [Fact]
    public async Task No_two_neighbouring_picks_share_an_album()
    {
        using var db = NewContext();
        AddAlbum(db, "Glory", 10);
        AddAlbum(db, "Circus", 10);
        AddAlbum(db, "Blackout", 10);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new MusicRecommendationRepository(db);
        var tracks = await repo.GetTopTracksByArtistAsync(_artistId, AllAccess, null, limit: 9, maxPerAlbum: 3);

        var albums = AlbumOf(tracks);
        albums.Should().HaveCount(9);

        for (var i = 1; i < albums.Count; i++)
        {
            albums[i].Should().NotBe(albums[i - 1], $"position {i} repeats the cover already shown at {i - 1}");
        }
    }

    // Interleaving must not cost tracks: the same set comes back, reordered.
    [Fact]
    public async Task The_cap_per_album_still_holds()
    {
        using var db = NewContext();
        AddAlbum(db, "Glory", 10);
        AddAlbum(db, "Circus", 10);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new MusicRecommendationRepository(db);
        var tracks = await repo.GetTopTracksByArtistAsync(_artistId, AllAccess, null, limit: 50, maxPerAlbum: 2);

        tracks.Should().HaveCount(4);
        AlbumOf(tracks).Distinct().Should().HaveCount(2);
    }

    // An artist with one album cannot be interleaved with anything, and must not
    // come back empty or loop forever trying.
    [Fact]
    public async Task A_single_album_artist_still_fills_the_row()
    {
        using var db = NewContext();
        AddAlbum(db, "Glory", 5);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new MusicRecommendationRepository(db);
        var tracks = await repo.GetTopTracksByArtistAsync(_artistId, AllAccess, null, limit: 24, maxPerAlbum: 3);

        tracks.Should().HaveCount(3);
    }

    [Fact]
    public async Task Asking_for_more_than_exists_returns_what_there_is()
    {
        using var db = NewContext();
        AddAlbum(db, "Glory", 2);
        AddAlbum(db, "Circus", 1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repo = new MusicRecommendationRepository(db);
        var tracks = await repo.GetTopTracksByArtistAsync(_artistId, AllAccess, null, limit: 24, maxPerAlbum: 5);

        tracks.Should().HaveCount(3);
    }
}
