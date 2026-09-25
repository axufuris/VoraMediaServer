using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// Which artists and albums a scan asks the artwork providers about: something
// missing AND not asked in the retry window. Missing alone matched nearly the
// whole library on every scan, because some artwork is never available.
public class MusicArtworkRefreshDueTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private static readonly DateTime CheckedBefore = DateTime.UtcNow.AddDays(-30);

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("artwork-due-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Set<MediaLibrary>().Add(new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } });
        return db;
    }

    private Artist AddArtist(VoraDbContext db, string name, bool complete, DateTime? checkedAt)
    {
        var artist = new Artist
        {
            Id = Guid.NewGuid(), Name = name, LibraryId = _libraryId, ArtworkUrl = "thumb.jpg", BackgroundUrl = "bg.jpg",
            BannerUrl = complete ? "banner.jpg" : null, ClearLogoUrl = complete ? "logo.png" : null, ArtworkCheckedAt = checkedAt
        };
        db.Set<Artist>().Add(artist);
        return artist;
    }

    [Fact]
    public async Task Artists_missing_artwork_are_asked_again_only_after_the_retry_window()
    {
        await using var db = NewContext();
        var never = AddArtist(db, "A Never Asked", complete: false, checkedAt: null);
        AddArtist(db, "B Asked Last Week", complete: false, checkedAt: DateTime.UtcNow.AddDays(-7));
        var stale = AddArtist(db, "C Asked Long Ago", complete: false, checkedAt: DateTime.UtcNow.AddDays(-45));
        AddArtist(db, "D Complete", complete: true, checkedAt: null);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var due = await new MusicRepository(db).GetArtistIdsForArtworkRefreshAsync(_libraryId, force: false, CheckedBefore);

        due.Should().Equal(never.Id, stale.Id);
    }

    [Fact]
    public async Task Forcing_asks_about_every_artist()
    {
        await using var db = NewContext();
        AddArtist(db, "A", complete: false, checkedAt: DateTime.UtcNow);
        AddArtist(db, "B", complete: true, checkedAt: DateTime.UtcNow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var due = await new MusicRepository(db).GetArtistIdsForArtworkRefreshAsync(_libraryId, force: true, CheckedBefore);

        due.Should().HaveCount(2);
    }

    // No provider has album backgrounds, so this is every album in the library.
    [Fact]
    public async Task An_album_with_no_background_is_not_asked_again_every_scan()
    {
        await using var db = NewContext();
        var artist = AddArtist(db, "311", complete: true, checkedAt: null);
        var fresh = new Album { Id = Guid.NewGuid(), Title = "Grassroots", ArtistId = artist.Id, LibraryId = _libraryId, ArtworkUrl = "c.jpg", DiscArtUrl = "d.png" };
        var recent = new Album { Id = Guid.NewGuid(), Title = "Transistor", ArtistId = artist.Id, LibraryId = _libraryId, ArtworkUrl = "c.jpg", DiscArtUrl = "d.png", ArtworkCheckedAt = DateTime.UtcNow.AddDays(-2) };
        db.Set<Album>().AddRange(fresh, recent);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var due = await new MusicRepository(db).GetAlbumIdsForArtworkRefreshAsync(_libraryId, force: false, CheckedBefore);

        due.Should().Equal(fresh.Id);
    }
}
