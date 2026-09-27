using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// The clients built their artist "Tracks" section as the first ten of
// GetTracksForArtistAsync, which orders by album year — so it was tracks 1-10 of
// the artist's oldest album, sitting directly above an Albums rail whose first
// card was that same album.
//
// The ordering here is a LEFT join for a reason that only shows up on a library
// nobody has played: an inner join, which is right for GetTopPlayedTracksAsync
// next door, would empty this section for every artist on a new install. No
// manual test on a used library would catch that, so it is the case tested
// hardest below.
public class ArtistTopTracksTests
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
        db.Set<Artist>().Add(new Artist { Id = _artistId, Name = "*NSYNC", LibraryId = _libraryId });
        return db;
    }

    private Guid AddAlbum(VoraDbContext db, string title, int year)
    {
        var id = Guid.NewGuid();
        db.Set<Album>().Add(new Album { Id = id, Title = title, Year = year, ArtistId = _artistId, LibraryId = _libraryId });
        return id;
    }

    private Track AddTrack(VoraDbContext db, Guid albumId, string title, int trackNumber, string? rating = null)
    {
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Title = title,
            AlbumId = albumId,
            TrackNumber = trackNumber,
            DiscNumber = 1,
            LibraryId = _libraryId,
            ContentRating = rating
        };
        db.Set<Track>().Add(track);
        return track;
    }

    private static void AddPlays(VoraDbContext db, Track track, int count, Guid? profileId = null, DateTime? at = null)
    {
        for (var i = 0; i < count; i++)
        {
            db.Set<TrackPlayHistory>().Add(new TrackPlayHistory
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                ProfileId = profileId ?? Guid.NewGuid(),
                PlayedAt = at ?? DateTime.UtcNow.AddMinutes(-i),
                DurationListenedSeconds = 200,
                Completed = true
            });
        }
    }

    private static MusicAccessFilter AllAccess => MusicAccessFilter.Unrestricted;

    // THE criterion that matters. An inner join silently breaks this, and a
    // manual test on a played library would never show it.
    [Fact]
    public async Task An_artist_nobody_has_played_still_returns_tracks_in_todays_order()
    {
        using var db = NewContext();
        var debut = AddAlbum(db, "*NSYNC", 1998);
        var later = AddAlbum(db, "No Strings Attached", 2000);
        AddTrack(db, later, "Bye Bye Bye", 1);
        AddTrack(db, debut, "Tearin' Up My Heart", 2);
        AddTrack(db, debut, "I Want You Back", 1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Should().NotBeEmpty("an unplayed artist must not render an empty section");
        tracks.Select(t => t.Title).Should().Equal("I Want You Back", "Tearin' Up My Heart", "Bye Bye Bye");
    }

    // An artist nobody here has played shows their actual hits, not the first
    // tracks of their oldest album — the slice this section replaced.
    [Fact]
    public async Task An_unplayed_artist_is_ordered_by_world_wide_popularity()
    {
        using var db = NewContext();
        var debut = AddAlbum(db, "*NSYNC", 1998);
        var later = AddAlbum(db, "No Strings Attached", 2000);
        var deepCut = AddTrack(db, debut, "Deep Cut", 1);
        var hit = AddTrack(db, later, "Bye Bye Bye", 1);
        deepCut.GlobalListeners = 20_000;
        hit.GlobalListeners = 1_500_000;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Bye Bye Bye", "Deep Cut");
    }

    // This server's own listening outranks the world's: the section is about what
    // gets played here first, and falls back to global figures only after that.
    [Fact]
    public async Task Plays_here_outrank_popularity_elsewhere()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "No Strings Attached", 2000);
        var globalHit = AddTrack(db, album, "Global Hit", 1);
        var localFavourite = AddTrack(db, album, "Local Favourite", 2);
        globalHit.GlobalListeners = 5_000_000;
        AddPlays(db, localFavourite, 1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Local Favourite", "Global Hit");
    }

    [Fact]
    public async Task Tracks_with_no_figure_sink_below_those_with_one()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "Celebrity", 2001);
        AddTrack(db, album, "Unknown To Last.fm", 1);
        var known = AddTrack(db, album, "Known", 2);
        known.GlobalListeners = 10;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Known", "Unknown To Last.fm");
    }

    [Fact]
    public async Task Played_tracks_lead_in_descending_play_count()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "No Strings Attached", 2000);
        var one = AddTrack(db, album, "Track One", 1);
        var two = AddTrack(db, album, "Track Two", 2);
        var three = AddTrack(db, album, "Track Three", 3);
        AddPlays(db, two, 5);
        AddPlays(db, three, 9);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Track Three", "Track Two", "Track One");
        _ = one;
    }

    [Fact]
    public async Task Ties_break_on_most_recently_played()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "Celebrity", 2001);
        var stale = AddTrack(db, album, "Stale", 1);
        var fresh = AddTrack(db, album, "Fresh", 2);
        AddPlays(db, stale, 3, at: DateTime.UtcNow.AddDays(-30));
        AddPlays(db, fresh, 3, at: DateTime.UtcNow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Take(2).Should().Equal("Fresh", "Stale");
    }

    // Unplayed tracks sit behind played ones rather than being dropped, so the
    // section is a full list that happens to be ordered, not a filtered one.
    [Fact]
    public async Task Unplayed_tracks_come_back_behind_the_played_ones()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "Celebrity", 2001);
        AddTrack(db, album, "Never Played", 1);
        var played = AddTrack(db, album, "Played Once", 2);
        AddPlays(db, played, 1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Played Once", "Never Played");
    }

    // Server-wide, not per-profile: this is a household server, and the page
    // already carries "Also Played Here", which is explicitly about everyone.
    [Fact]
    public async Task Plays_from_every_profile_count_toward_the_same_order()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "No Strings Attached", 2000);
        var mine = AddTrack(db, album, "Mine", 1);
        var theirs = AddTrack(db, album, "Theirs", 2);
        AddPlays(db, mine, 2, profileId: Guid.NewGuid());
        AddPlays(db, theirs, 4, profileId: Guid.NewGuid());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Select(t => t.Title).Should().Equal("Theirs", "Mine");
    }

    [Fact]
    public async Task An_artist_with_no_albums_returns_an_empty_list()
    {
        using var db = NewContext();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(_artistId, AllAccess, 10);

        tracks.Should().BeEmpty();
    }

    [Fact]
    public async Task The_limit_is_clamped_rather_than_trusted()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "Greatest Hits", 2005);
        for (var i = 1; i <= 60; i++) AddTrack(db, album, $"Track {i:00}", i);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new MusicRepository(db);

        (await repository.GetTopTracksForArtistAsync(_artistId, AllAccess, 500)).Should().HaveCount(50);
        (await repository.GetTopTracksForArtistAsync(_artistId, AllAccess, 0)).Should().HaveCount(1);
        (await repository.GetTopTracksForArtistAsync(_artistId, AllAccess, -5)).Should().HaveCount(1);
    }

    // A track the profile may not see is absent, and must not push another track
    // down the list on its way out.
    [Fact]
    public async Task A_track_the_profile_cannot_see_is_absent_and_does_not_hold_a_position()
    {
        using var db = NewContext();
        var album = AddAlbum(db, "Celebrity", 2001);
        var blocked = AddTrack(db, album, "Unrated Track", 1);
        AddTrack(db, album, "Rated Track", 2, rating: "PG");
        AddPlays(db, blocked, 50);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRepository(db).GetTopTracksForArtistAsync(
            _artistId,
            new MusicAccessFilter { HasAllLibraryAccess = true, BlockUnratedContent = true },
            10);

        tracks.Select(t => t.Title).Should().Equal("Rated Track");
    }
}
