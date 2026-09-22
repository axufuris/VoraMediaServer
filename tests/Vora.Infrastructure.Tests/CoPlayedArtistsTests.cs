using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// "Also Played Here" asks what this SERVER plays alongside an artist — every
// profile's history, not the current viewer's. The Music page's For You tab is
// the personal view; this row exists to answer the other question.
public class CoPlayedArtistsTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("co-played-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private readonly Guid _libraryId = Guid.NewGuid();

    private Artist SeedArtist(VoraDbContext db, string name)
    {
        var artist = new Artist { Id = Guid.NewGuid(), Name = name, LibraryId = _libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = name + " Album", ArtistId = artist.Id, LibraryId = _libraryId };
        var track = new Track { Id = Guid.NewGuid(), Title = name + " Track", AlbumId = album.Id, LibraryId = _libraryId };
        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        db.Set<Track>().Add(track);
        return artist;
    }

    private Guid TrackOf(VoraDbContext db, Artist artist) =>
        db.Set<Track>().Local.First(t => db.Set<Album>().Local.First(a => a.Id == t.AlbumId).ArtistId == artist.Id).Id;

    private void Play(VoraDbContext db, Guid profileId, Guid trackId, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            db.Set<TrackPlayHistory>().Add(new TrackPlayHistory
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                TrackId = trackId,
                PlayedAt = DateTime.UtcNow,
                Completed = true,
            });
        }
    }

    private Guid SeedProfile(VoraDbContext db)
    {
        var id = Guid.NewGuid();
        db.Set<UserProfile>().Add(new UserProfile { Id = id, Name = "P" + id.ToString("N")[..4], UserId = Guid.NewGuid() });
        return id;
    }

    private void SeedLibrary(VoraDbContext db) =>
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = _libraryId,
            Name = "Music",
            Type = LibraryType.Music,
            FolderPaths = new List<string> { "/media/music" }
        });

    [Fact]
    public async Task Returns_artists_played_by_the_same_listeners()
    {
        using var db = NewContext();
        SeedLibrary(db);
        var seed = SeedArtist(db, "311");
        var also = SeedArtist(db, "Sublime");
        var unrelated = SeedArtist(db, "Eminem");
        db.SaveChanges();

        var profile = SeedProfile(db);
        Play(db, profile, TrackOf(db, seed));
        Play(db, profile, TrackOf(db, also));
        db.SaveChanges();

        var repo = new MusicRepository(db);
        var result = await repo.GetCoPlayedArtistsAsync(seed.Id, MusicAccessFilter.Unrestricted, 12);

        result.Select(a => a.Name).Should().Contain("Sublime");
        result.Select(a => a.Name).Should().NotContain("Eminem");
    }

    [Fact]
    public async Task Never_returns_the_artist_being_viewed()
    {
        using var db = NewContext();
        SeedLibrary(db);
        var seed = SeedArtist(db, "311");
        db.SaveChanges();

        var profile = SeedProfile(db);
        Play(db, profile, TrackOf(db, seed), times: 5);
        db.SaveChanges();

        var repo = new MusicRepository(db);
        var result = await repo.GetCoPlayedArtistsAsync(seed.Id, MusicAccessFilter.Unrestricted, 12);

        result.Should().NotContain(a => a.Id == seed.Id);
    }

    // Server-wide, not per-profile: a second profile's overlap has to count.
    [Fact]
    public async Task Counts_every_profile_on_the_server()
    {
        using var db = NewContext();
        SeedLibrary(db);
        var seed = SeedArtist(db, "311");
        var also = SeedArtist(db, "Sublime");
        db.SaveChanges();

        var other = SeedProfile(db);
        Play(db, other, TrackOf(db, seed));
        Play(db, other, TrackOf(db, also));
        db.SaveChanges();

        var repo = new MusicRepository(db);
        var result = await repo.GetCoPlayedArtistsAsync(seed.Id, MusicAccessFilter.Unrestricted, 12);

        result.Select(a => a.Name).Should().Contain("Sublime");
    }

    // One profile looping an album must not outrank an artist several people share.
    [Fact]
    public async Task Ranks_shared_listeners_above_raw_play_count()
    {
        using var db = NewContext();
        SeedLibrary(db);
        var seed = SeedArtist(db, "311");
        var shared = SeedArtist(db, "Shared");
        var looped = SeedArtist(db, "Looped");
        db.SaveChanges();

        var a = SeedProfile(db);
        var b = SeedProfile(db);
        Play(db, a, TrackOf(db, seed));
        Play(db, b, TrackOf(db, seed));

        Play(db, a, TrackOf(db, shared));
        Play(db, b, TrackOf(db, shared));
        Play(db, a, TrackOf(db, looped), times: 50);
        db.SaveChanges();

        var repo = new MusicRepository(db);
        var result = await repo.GetCoPlayedArtistsAsync(seed.Id, MusicAccessFilter.Unrestricted, 12);

        result.Select(a2 => a2.Name).First().Should().Be("Shared");
    }

    [Fact]
    public async Task Returns_nothing_when_the_artist_has_never_been_played()
    {
        using var db = NewContext();
        SeedLibrary(db);
        var seed = SeedArtist(db, "311");
        SeedArtist(db, "Sublime");
        db.SaveChanges();

        var repo = new MusicRepository(db);
        var result = await repo.GetCoPlayedArtistsAsync(seed.Id, MusicAccessFilter.Unrestricted, 12);

        result.Should().BeEmpty();
    }
}
