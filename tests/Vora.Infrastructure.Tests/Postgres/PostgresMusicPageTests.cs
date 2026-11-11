using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests.Postgres;

// The album and artist pages each loaded their songs, then the profile's likes
// of them, then its ratings of them, one query each, and the artist pages
// looked up the artist's album ids first. The marks now come with the songs.
public class PostgresMusicPageTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private sealed class Counter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private static MusicManager Manager(VoraDbContext db) => new(
        new MusicRepository(db),
        Substitute.For<IUserRepository>(),
        Array.Empty<IMusicArtworkProvider>(),
        Array.Empty<ILyricsProvider>(),
        Array.Empty<IListeningDataProvider>(),
        Substitute.For<IClientNotifier>(),
        Options.Create(new StoragePathsOptions()),
        new NullTaskProgressReporter(),
        NullLogger<MusicManager>.Instance);

    private sealed record Shelf(
        Guid ProfileId, Guid LibraryId, Guid ArtistId, Guid OldAlbumId, Guid NewAlbumId, Guid BorrowingAlbumId,
        Track[] NewSongs, Track[] OldSongs);

    // An artist with an album from 2001 (no backdrop, no cover) and one from 1999,
    // and an album in the open library by an artist kept in a hidden one. The
    // profile likes and rates some of it.
    private async Task<Shelf> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.NewContext();
        var open = new MediaLibrary { Name = "Music " + tag, Type = LibraryType.Music, FolderPaths = ["/music"] };
        var hidden = new MediaLibrary { Name = "Hidden " + tag, Type = LibraryType.Music, FolderPaths = ["/hidden"] };
        var user = new User { Email = $"{tag}@example.com", DisplayName = tag };
        var profile = new UserProfile { Name = "Listener", UserId = user.Id };
        var artist = new Artist { Name = "Fall Out Boy " + tag, LibraryId = open.Id, ArtworkUrl = "/fob.jpg", BackgroundUrl = "/fob-wide.jpg" };
        var stranger = new Artist { Name = "Stranger " + tag, LibraryId = hidden.Id, BackgroundUrl = "/stranger-wide.jpg" };
        var newer = new Album { Title = "Take This to Your Grave", Year = 2001, ArtistId = artist.Id, LibraryId = open.Id };
        var older = new Album { Title = "Evening Out", Year = 1999, ArtistId = artist.Id, LibraryId = open.Id, ArtworkUrl = "/evening.jpg", BackgroundUrl = "/evening-wide.jpg" };
        var borrowing = new Album { Title = "Guest Spot", Year = 2005, ArtistId = stranger.Id, LibraryId = open.Id };
        Track Song(Album album, int number, params string[] moods) => new()
        {
            Title = $"{album.Title} {number}",
            AlbumId = album.Id,
            LibraryId = open.Id,
            DiscNumber = 1,
            TrackNumber = number,
            Moods = moods.Length == 0 ? null : moods.ToList()
        };
        var newSongs = new[] { Song(newer, 1, "happy"), Song(newer, 2), Song(newer, 3, "happy") };
        var oldSongs = new[] { Song(older, 1), Song(older, 2) };

        db.AddRange(open, hidden, user, profile, artist, stranger, newer, older, borrowing);
        db.AddRange(newSongs);
        db.AddRange(oldSongs);
        db.Add(new ArtistTag { ArtistId = artist.Id, Tag = "emo", Weight = 1 });
        db.Add(new ArtistTag { ArtistId = artist.Id, Tag = "pop punk", Weight = 2 });
        db.Add(new TrackLike { ProfileId = profile.Id, TrackId = newSongs[0].Id });
        db.Add(new TrackLike { ProfileId = profile.Id, TrackId = oldSongs[0].Id });
        db.Add(new UserMediaRating { ProfileId = profile.Id, MediaItemId = newSongs[1].Id, Rating = 7.5m });
        db.Add(new UserMediaRating { ProfileId = profile.Id, MediaItemId = oldSongs[0].Id, Rating = 9m });
        db.Add(new UserAlbumRating { ProfileId = profile.Id, AlbumId = newer.Id, Rating = 8m });
        db.Add(new UserArtistRating { ProfileId = profile.Id, ArtistId = artist.Id, Rating = 6.5m });
        for (var i = 0; i < 3; i++) db.Add(new TrackPlayHistory { ProfileId = profile.Id, TrackId = newSongs[2].Id, Completed = true });
        db.Add(new TrackPlayHistory { ProfileId = profile.Id, TrackId = oldSongs[1].Id, Completed = true });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Shelf(profile.Id, open.Id, artist.Id, older.Id, newer.Id, borrowing.Id, newSongs, oldSongs);
    }

    private static MusicAccessFilter Only(Guid libraryId) => new() { HasAllLibraryAccess = false, AllowedLibraryIds = [libraryId] };

    [Fact]
    public async Task An_album_page_is_two_queries_and_carries_the_profiles_likes_and_ratings()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var page = await Manager(db).GetAlbumDetailAsync(shelf.NewAlbumId, shelf.ProfileId, MusicAccessFilter.Unrestricted);

        counter.Count.Should().Be(2);
        page!.Album.MyRating.Should().Be(8m);
        page.ArtistBackgroundUrl.Should().Be("/fob-wide.jpg", "the album has no backdrop of its own");
        page.Album.ArtistArtworkUrl.Should().Be("/fob.jpg");
        page.Tracks.Select(t => t.Id).Should().Equal(shelf.NewSongs.Select(s => s.Id));
        page.Tracks.Select(t => t.IsLiked).Should().Equal(true, false, false);
        page.Tracks.Select(t => t.MyRating).Should().Equal(null, 7.5m, null);
    }

    [Fact]
    public async Task An_artist_page_is_two_queries_and_carries_the_profiles_ratings()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var page = await Manager(db).GetArtistDetailAsync(shelf.ArtistId, shelf.ProfileId, MusicAccessFilter.Unrestricted);

        counter.Count.Should().Be(2);
        page!.Artist.MyRating.Should().Be(6.5m);
        page.Tags.Should().Equal("pop punk", "emo");
        page.Albums.Select(a => a.Id).Should().Equal(shelf.OldAlbumId, shelf.NewAlbumId);
        page.Albums.Select(a => a.MyRating).Should().Equal(null, 8m);
    }

    [Fact]
    public async Task An_artists_songs_and_top_songs_are_one_query_each_with_their_albums()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var songs = await Manager(db).GetTracksForArtistAsync(shelf.ArtistId, shelf.ProfileId, MusicAccessFilter.Unrestricted);
        counter.Count.Should().Be(1);
        var top = await Manager(db).GetTopTracksForArtistAsync(shelf.ArtistId, shelf.ProfileId, MusicAccessFilter.Unrestricted, 10);
        counter.Count.Should().Be(2);

        songs.Select(s => s.Id).Should().Equal(shelf.OldSongs.Concat(shelf.NewSongs).Select(s => s.Id), "the 1999 album comes first");
        songs.Select(s => s.IsLiked).Should().Equal(true, false, true, false, false);
        songs.Select(s => s.MyRating).Should().Equal(9m, null, null, 7.5m, null);
        songs.Select(s => s.AlbumArtworkUrl).Should().Equal("/evening.jpg", "/evening.jpg", "/fob.jpg", "/fob.jpg", "/fob.jpg");
        top.Select(s => s.Id).Should().Equal(
            new[] { shelf.NewSongs[2], shelf.OldSongs[1], shelf.OldSongs[0], shelf.NewSongs[0], shelf.NewSongs[1] }.Select(s => s.Id),
            "most played first, then the oldest album's songs in order");
        top.Single(s => s.Id == shelf.OldSongs[0].Id).Should().BeEquivalentTo(new { IsLiked = true, MyRating = 9m, AlbumTitle = "Evening Out" });
    }

    [Fact]
    public async Task With_no_profile_nothing_is_marked()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();
        await using var db = database.NewContext();
        var manager = Manager(db);

        var album = await manager.GetAlbumDetailAsync(shelf.NewAlbumId, null, MusicAccessFilter.Unrestricted);
        var artist = await manager.GetArtistDetailAsync(shelf.ArtistId, null, MusicAccessFilter.Unrestricted);
        var songs = await manager.GetTracksForArtistAsync(shelf.ArtistId, null, MusicAccessFilter.Unrestricted);

        album!.Album.MyRating.Should().BeNull();
        album.Tracks.Should().OnlyContain(t => !t.IsLiked && t.MyRating == null);
        artist!.Artist.MyRating.Should().BeNull();
        artist.Albums.Should().OnlyContain(a => a.MyRating == null);
        songs.Should().OnlyContain(t => !t.IsLiked && t.MyRating == null);
    }

    // The artist's backdrop is read through the access filter in the same
    // query, so a profile that may not see the artist gets none.
    [Fact]
    public async Task An_artist_the_profile_cannot_see_lends_the_album_no_backdrop()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();
        await using var db = database.NewContext();
        var manager = Manager(db);

        (await manager.GetAlbumDetailAsync(shelf.BorrowingAlbumId, shelf.ProfileId, Only(shelf.LibraryId)))!
            .ArtistBackgroundUrl.Should().BeNull();
        (await manager.GetAlbumDetailAsync(shelf.BorrowingAlbumId, shelf.ProfileId, MusicAccessFilter.Unrestricted))!
            .ArtistBackgroundUrl.Should().Be("/stranger-wide.jpg");
        (await manager.GetAlbumDetailAsync(shelf.OldAlbumId, shelf.ProfileId, MusicAccessFilter.Unrestricted))!
            .ArtistBackgroundUrl.Should().BeNull("the album has its own");
    }

    [Fact]
    public async Task A_moods_songs_carry_the_profiles_marks_from_one_more_query()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var shelf = await SeedAsync();

        var counter = new Counter();
        await using var db = database.NewContext(counter);
        var mood = await Manager(db).GetMoodTracksAsync("happy", shelf.ProfileId, Only(shelf.LibraryId), 0, 10);

        counter.Count.Should().Be(3, "the count, the page of songs, and the marks");
        mood!.Tracks.Select(t => (t.Id, t.IsLiked)).Should().BeEquivalentTo(new[] { (shelf.NewSongs[0].Id, true), (shelf.NewSongs[2].Id, false) });
    }
}
