using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// These titles are what a running task shows as its progress line, so they have
// to identify the item on their own. An episode is already qualified by its show
// and number; a track was falling through to its bare title, which in a music
// library is routinely ambiguous — half a library has an "Intro".
public class DisplayTitleTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("display-title-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedTrack(VoraDbContext db, string artist, string album, string title)
    {
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Music",
            Type = LibraryType.Music,
            FolderPaths = new List<string> { "/media/music" }
        });

        var artistRow = new Artist { Id = Guid.NewGuid(), Name = artist, LibraryId = libraryId };
        var albumRow = new Album { Id = Guid.NewGuid(), Title = album, ArtistId = artistRow.Id, LibraryId = libraryId };
        var track = new Track { Id = Guid.NewGuid(), Title = title, AlbumId = albumRow.Id, LibraryId = libraryId };

        db.Set<Artist>().Add(artistRow);
        db.Set<Album>().Add(albumRow);
        db.Set<Track>().Add(track);
        db.SaveChanges();
        return track.Id;
    }

    [Fact]
    public async Task A_track_is_named_by_its_artist_and_album()
    {
        using var db = NewContext();
        var trackId = SeedTrack(db, "311", "Grassroots", "We're in This Together");
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var titles = await repo.GetDisplayTitlesByIdsAsync(new[] { trackId });

        titles[trackId].Should().Be("311 — Grassroots — We're in This Together");
    }

    [Fact]
    public async Task Tracks_that_share_a_title_are_still_told_apart()
    {
        using var db = NewContext();
        var first = SeedTrack(db, "Beastie Boys", "Hello Nasty", "Intro");
        var second = SeedTrack(db, "Foo Fighters", "The Colour and the Shape", "Intro");
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var titles = await repo.GetDisplayTitlesByIdsAsync(new[] { first, second });

        titles[first].Should().NotBe(titles[second]);
        titles[first].Should().StartWith("Beastie Boys — Hello Nasty");
        titles[second].Should().StartWith("Foo Fighters — The Colour and the Shape");
    }

    [Fact]
    public async Task A_movie_keeps_its_plain_title()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        });
        var movie = new Movie { Id = Guid.NewGuid(), Title = "Inception", LibraryId = libraryId };
        db.Set<Movie>().Add(movie);
        db.SaveChanges();

        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var titles = await repo.GetDisplayTitlesByIdsAsync(new[] { movie.Id });

        titles[movie.Id].Should().Be("Inception");
    }
}
