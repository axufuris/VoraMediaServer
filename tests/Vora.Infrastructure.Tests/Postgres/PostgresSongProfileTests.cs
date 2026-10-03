using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresSongProfileTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static float[] Vector(float value) => Enumerable.Repeat(value, 1536).ToArray();

    private static async Task<(Artist Artist, Album Album, Guid LibraryId)> AlbumAsync(VoraDbContext db, string artistName)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = artistName, LibraryId = library.Id };
        var album = new Album { Id = Guid.NewGuid(), Title = "Discovery", ArtistId = artist.Id, LibraryId = library.Id, Year = 2001, Genre = "Electronic" };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (artist, album, library.Id);
    }

    private static async Task<Track> TrackAsync(VoraDbContext db, Album album, Guid libraryId, string title)
    {
        var track = new Track { Id = Guid.NewGuid(), Title = title, AlbumId = album.Id, LibraryId = libraryId };
        db.Set<Track>().Add(track);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return track;
    }

    [Fact]
    public async Task A_saved_profile_reads_back_and_the_song_is_no_longer_pending()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (_, album, libraryId) = await AlbumAsync(db, "Daft Punk");
        var profiled = await TrackAsync(db, album, libraryId, "One More Time");
        var pending = await TrackAsync(db, album, libraryId, "Aerodynamic");

        var saved = await new MusicRepository(db).SaveTrackProfilesAsync(new[]
        {
            new TrackProfileUpdate(profiled.Id, new List<string> { "euphoric", "upbeat" }, TrackEnergy.High, new List<string> { "partying" }, new List<string> { "party" }, false),
            new TrackProfileUpdate(Guid.NewGuid(), new List<string> { "gone" }, TrackEnergy.Low, new List<string>(), new List<string>(), false),
        });

        saved.Should().Be(1);
        var fresh = new MusicRepository(database.NewContext());
        (await fresh.GetTracksNeedingProfilesAsync()).Select(t => t.TrackId).Should().Contain(pending.Id).And.NotContain(profiled.Id);
        var descriptor = (await fresh.GetTrackDescriptorsAsync()).Single(d => d.TrackId == profiled.Id);
        descriptor.Moods.Should().Equal("euphoric", "upbeat");
        descriptor.Energy.Should().Be(TrackEnergy.High);
        descriptor.Genre.Should().Be("Electronic");
        descriptor.AlbumArtistId.Should().Be(album.ArtistId);
        descriptor.EmbeddedHash.Should().BeNull();
    }

    [Fact]
    public async Task Embeddings_are_inserted_then_replaced_when_the_description_changes()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (_, album, libraryId) = await AlbumAsync(db, "Justice");
        var track = await TrackAsync(db, album, libraryId, "D.A.N.C.E.");
        var repository = new MusicRepository(db);

        (await repository.SaveTrackEmbeddingsAsync(new[] { new TrackEmbeddingUpdate(track.Id, Vector(0.1f), "first", "text-embedding-3-small") })).Should().Be(1);
        (await repository.SaveTrackEmbeddingsAsync(new[]
        {
            new TrackEmbeddingUpdate(track.Id, Vector(0.2f), "second", "text-embedding-3-small"),
            new TrackEmbeddingUpdate(Guid.NewGuid(), Vector(0.3f), "orphan", "text-embedding-3-small"),
        })).Should().Be(1);

        await using var check = database.NewContext();
        var row = await check.MediaItemEmbeddings.AsNoTracking().SingleAsync(e => e.MediaItemId == track.Id, TestContext.Current.CancellationToken);
        row.SourceHash.Should().Be("second");
        row.Embedding!.ToArray()[0].Should().BeApproximately(0.2f, 1e-6f);
        (await new MusicRepository(check).GetTrackDescriptorsAsync()).Single(d => d.TrackId == track.Id).EmbeddedHash.Should().Be("second");
    }

    [Fact]
    public async Task Tags_come_back_per_artist_most_used_first_and_capped()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (artist, _, _) = await AlbumAsync(db, "Fall Out Boy");
        var repository = new MusicRepository(db);
        await repository.StageArtistTagsAsync(artist.Id, new[] { "pop punk", "rock", "emo", "alternative" });
        await repository.SaveMusicChangesAsync(TestContext.Current.CancellationToken);

        var tags = await new MusicRepository(database.NewContext()).GetAllArtistTagNamesAsync(2);

        tags[artist.Id].Should().Equal("pop punk", "rock");
    }

    [Fact]
    public async Task Songs_to_order_carry_their_energy_and_mood()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var (_, album, libraryId) = await AlbumAsync(db, "Daft Punk");
        var track = await TrackAsync(db, album, libraryId, "Digital Love");
        await new MusicRepository(db).SaveTrackProfilesAsync(new[]
        {
            new TrackProfileUpdate(track.Id, new List<string> { "romantic" }, TrackEnergy.Medium, new List<string>(), new List<string>(), false)
        });

        var songs = await new AiPlaylistRepository(database.NewContext()).GetTracksForOrderingAsync(new[] { track.Id });

        songs.Single().Should().Be(songs.Single() with { Title = "Digital Love", Artist = "Daft Punk", Energy = TrackEnergy.Medium });
        songs.Single().Moods.Should().Equal("romantic");
    }
}
