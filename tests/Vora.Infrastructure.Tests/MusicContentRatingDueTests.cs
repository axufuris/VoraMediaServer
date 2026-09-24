using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// Which albums the rating job asks a provider about. A rating from the file is
// never asked about, a recent "don't know" is not asked again, and albums never
// asked come first so a run cut short still spends itself on the unrated.
public class MusicContentRatingDueTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private static readonly DateTime RecheckBefore = DateTime.UtcNow.AddDays(-90);

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("rating-due-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Set<MediaLibrary>().Add(new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } });
        db.Set<Artist>().Add(new Artist { Id = _artistId, Name = "Eminem", LibraryId = _libraryId });
        return db;
    }

    private void Album(VoraDbContext db, string title, string? rating, DateTime? checkedAt = null)
    {
        var album = new Album { Id = Guid.NewGuid(), Title = title, ArtistId = _artistId, LibraryId = _libraryId };
        db.Set<Album>().Add(album);
        db.Set<Track>().Add(new Track
        {
            Id = Guid.NewGuid(), Title = title + " track", AlbumId = album.Id, TrackNumber = 1, LibraryId = _libraryId,
            ContentRating = rating, ContentRatingCheckedAt = checkedAt, AddedAt = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task Only_unrated_albums_not_asked_recently_are_due_never_asked_first()
    {
        await using var db = NewContext();
        Album(db, "A Tagged", "Explicit");
        Album(db, "B Asked Last Week", null, DateTime.UtcNow.AddDays(-7));
        Album(db, "C Asked Long Ago", null, DateTime.UtcNow.AddDays(-200));
        Album(db, "D Never Asked", null);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var due = await new MusicRepository(db).GetAlbumsDueForContentRatingAsync(RecheckBefore, 10);

        due.Select(d => d.AlbumTitle).Should().Equal("D Never Asked", "C Asked Long Ago");
        due[0].ArtistName.Should().Be("Eminem");
    }
}
