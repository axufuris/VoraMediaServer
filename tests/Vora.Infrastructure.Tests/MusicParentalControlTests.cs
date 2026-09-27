using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// Seven recommendation queries checked "block unrated" but never the Clean/
// Explicit allowlist, so a profile permitted only Clean music was handed Explicit
// tracks — titles and artwork visible — in radio, stations, Daily and Weekly
// Mixes, "Because you played" and genre pages. The stream endpoint still refused
// to play them, which is why it went unnoticed: the queue just broke when it
// reached one.
public class MusicParentalControlTests
{
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private Guid _albumId;

    private static MusicAccessFilter CleanOnly => new() { AllowedRatings = new List<string> { "Clean" } };

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("parental-" + Guid.NewGuid().ToString("N"))
            .Options);

        db.Set<MediaLibrary>().Add(new MediaLibrary { Id = _libraryId, Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } });
        db.Set<Artist>().Add(new Artist { Id = _artistId, Name = "Eminem", LibraryId = _libraryId });
        _albumId = Guid.NewGuid();
        db.Set<Album>().Add(new Album { Id = _albumId, Title = "The Eminem Show", Genre = "Hip-Hop", ArtistId = _artistId, LibraryId = _libraryId });
        return db;
    }

    private void AddTrack(VoraDbContext db, string title, int number, string? rating) =>
        db.Set<Track>().Add(new Track
        {
            Id = Guid.NewGuid(),
            Title = title,
            AlbumId = _albumId,
            TrackNumber = number,
            LibraryId = _libraryId,
            ContentRating = rating,
            AddedAt = DateTime.UtcNow
        });

    private async Task<VoraDbContext> Seeded()
    {
        var db = NewContext();
        AddTrack(db, "Without Me (Explicit)", 1, "Explicit");
        AddTrack(db, "Without Me (Clean)", 2, "Clean");
        AddTrack(db, "Untagged", 3, null);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    // Feeds radio, stations and "Because you played".
    [Fact]
    public async Task Top_tracks_by_artist_leaves_explicit_out_of_a_clean_only_profile()
    {
        using var db = await Seeded();

        var tracks = await new MusicRecommendationRepository(db).GetTopTracksByArtistAsync(_artistId, CleanOnly, null, 50, 50);

        tracks.Select(t => t.Title).Should().NotContain("Without Me (Explicit)");
        tracks.Select(t => t.Title).Should().Contain(new[] { "Without Me (Clean)", "Untagged" });
    }

    // Feeds genre pages and genre stations.
    [Fact]
    public async Task Tracks_by_genre_leaves_explicit_out_of_a_clean_only_profile()
    {
        using var db = await Seeded();

        var tracks = await new MusicRecommendationRepository(db).GetTracksByGenreAsync("Hip-Hop", CleanOnly, Array.Empty<Guid>(), 50);

        tracks.Select(t => t.Title).Should().NotContain("Without Me (Explicit)");
    }

    // Mixes store track ids and hydrate them here — so a mix generated before a
    // restriction was added must not bring the Explicit track back.
    [Fact]
    public async Task Hydrating_a_stored_mix_leaves_explicit_out_of_a_clean_only_profile()
    {
        using var db = await Seeded();
        var ids = await db.Set<Track>().Select(t => t.Id).ToListAsync(TestContext.Current.CancellationToken);

        var tracks = await new MusicRecommendationRepository(db).GetTracksByIdsAsync(ids, CleanOnly);

        tracks.Select(t => t.Title).Should().NotContain("Without Me (Explicit)");
        tracks.Should().HaveCount(2);
    }

    // The one query that did check the allowlist applied "block unrated" only
    // when the allowlist was ALSO restricted. They are independent controls.
    [Fact]
    public async Task Block_unrated_applies_even_without_an_allowlist()
    {
        using var db = await Seeded();
        var blockUnratedOnly = new MusicAccessFilter { BlockUnratedContent = true };

        var tracks = await new MusicRecommendationRepository(db).GetRecentlyAddedTracksByArtistsAsync(new[] { _artistId }, blockUnratedOnly, 30, 50);

        tracks.Select(t => t.Title).Should().NotContain("Untagged");
        tracks.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_unrestricted_profile_sees_everything()
    {
        using var db = await Seeded();

        var tracks = await new MusicRecommendationRepository(db).GetTopTracksByArtistAsync(_artistId, MusicAccessFilter.Unrestricted, null, 50, 50);

        tracks.Should().HaveCount(3);
    }
}
