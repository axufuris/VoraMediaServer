using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Tests;

// The whole design rests on a capability being an Expression rather than a
// method, because a method cannot be translated into SQL and fails at RUN time
// rather than build time. These execute the composed queries so that translation
// is proven rather than assumed.
public class MediaCapabilityQueryTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("capability-query-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private readonly Guid _libraryId = Guid.NewGuid();

    private void Seed(VoraDbContext db)
    {
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = _libraryId,
            Name = "Mixed",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media" }
        });

        var show = new TvShow { Id = Guid.NewGuid(), Title = "Severance", LibraryId = _libraryId };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", TvShowId = show.Id, LibraryId = _libraryId };
        var episode = new Episode { Id = Guid.NewGuid(), Title = "Half Loop", SeasonId = season.Id, LibraryId = _libraryId };

        var artist = new Artist { Id = Guid.NewGuid(), Name = "311", LibraryId = _libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = "Grassroots", ArtistId = artist.Id, LibraryId = _libraryId };
        var track = new Track { Id = Guid.NewGuid(), Title = "Lucky", AlbumId = album.Id, LibraryId = _libraryId };

        db.Set<Movie>().Add(new Movie { Id = Guid.NewGuid(), Title = "Inception", LibraryId = _libraryId });
        db.Set<TvShow>().Add(show);
        db.Set<Season>().Add(season);
        db.Set<Episode>().Add(episode);
        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        db.Set<Track>().Add(track);
        db.SaveChanges();
    }

    private static async Task<List<string>> TitlesOf(IQueryable<MediaItem> query) =>
        (await query.Select(m => m.Title).ToListAsync(TestContext.Current.CancellationToken)).OrderBy(t => t).ToList();

    [Fact]
    public async Task HasProviderIdentity_composes_into_a_query()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems.Where(MediaCapabilities.HasProviderIdentity));

        titles.Should().BeEquivalentTo(new[] { "Inception", "Severance" });
    }

    [Fact]
    public async Task SupportsMetadataEnrichment_composes_into_a_query()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems.Where(MediaCapabilities.SupportsMetadataEnrichment));

        titles.Should().BeEquivalentTo(new[] { "Half Loop", "Inception", "Season 1", "Severance" });
        titles.Should().NotContain("Lucky");
    }

    [Fact]
    public async Task HasPlayableParts_composes_into_a_query()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems.Where(MediaCapabilities.HasPlayableParts));

        titles.Should().BeEquivalentTo(new[] { "Half Loop", "Inception", "Lucky" });
    }

    // The migrated call sites chain a capability with the rest of their filter
    // rather than inlining it, so that shape has to translate too.
    [Fact]
    public async Task A_capability_chains_with_other_predicates()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems
            .Where(MediaCapabilities.HasProviderIdentity)
            .Where(m => m.LibraryId == _libraryId && m.PosterUrl == null));

        titles.Should().BeEquivalentTo(new[] { "Inception", "Severance" });
    }

    [Fact]
    public async Task A_capability_composes_after_a_projection_free_filter()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems
            .Where(m => m.LibraryId == _libraryId)
            .Where(MediaCapabilities.IsBrowsableTitle));

        titles.Should().BeEquivalentTo(new[] { "Inception", "Severance" });
    }

    // The reason On() rebinds the parameter instead of invoking the expression is
    // that an Invoke node throws at query time, so the only proof that matters is
    // a query that actually runs.
    [Fact]
    public async Task On_filters_an_owning_entity_by_its_media_items_capability()
    {
        using var db = NewContext();
        Seed(db);

        var profileId = Guid.NewGuid();
        foreach (var item in await db.MediaItems.ToListAsync(TestContext.Current.CancellationToken))
        {
            db.UserMediaStates.Add(new UserMediaState
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                MediaItemId = item.Id
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var titles = (await db.UserMediaStates
            .Where(MediaCapabilities.IsBrowsableTitle.On((UserMediaState s) => s.MediaItem))
            .Select(s => s.MediaItem.Title)
            .ToListAsync(TestContext.Current.CancellationToken))
            .OrderBy(t => t)
            .ToList();

        titles.Should().BeEquivalentTo(new[] { "Inception", "Severance" });
    }

    [Fact]
    public async Task IsPartOfATvShow_composes_into_a_query()
    {
        using var db = NewContext();
        Seed(db);

        var titles = await TitlesOf(db.MediaItems.Where(MediaCapabilities.IsPartOfATvShow));

        titles.Should().BeEquivalentTo(new[] { "Half Loop", "Season 1", "Severance" });
        titles.Should().NotContain("Inception");
    }

    // The query form and the in-memory form have to agree, or the same item can
    // be included by a sweep and rejected by the code that then processes it.
    [Fact]
    public async Task The_query_form_and_the_entity_form_select_the_same_items()
    {
        using var db = NewContext();
        Seed(db);

        var fromQuery = await TitlesOf(db.MediaItems.Where(MediaCapabilities.SupportsMetadataEnrichment));
        var all = await db.MediaItems.ToListAsync(TestContext.Current.CancellationToken);
        var fromEntities = all.Where(m => m.CanBeEnriched()).Select(m => m.Title).OrderBy(t => t).ToList();

        fromQuery.Should().BeEquivalentTo(fromEntities);
    }
}
