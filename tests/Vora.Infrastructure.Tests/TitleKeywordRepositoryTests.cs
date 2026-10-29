using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class TitleKeywordRepositoryTests
{
    private readonly Guid _library = Guid.NewGuid();

    private async Task<VoraDbContext> SeededAsync(params MediaItem[] items)
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("title-keywords-" + Guid.NewGuid().ToString("N"))
            .Options);
        db.Add(new MediaLibrary { Id = _library, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });
        db.AddRange(items);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task Only_films_and_shows_with_a_tmdb_id_that_were_never_asked_wait_for_keywords()
    {
        var waiting = new TvShow { Title = "Community", TmdbId = "18347", LibraryId = _library };
        var answered = new TvShow { Title = "Abed's Show", TmdbId = "1", LibraryId = _library, Keywords = new() };
        var noTmdb = new TvShow { Title = "Home Movies", LibraryId = _library };
        var gone = new TvShow { Title = "Gone", TmdbId = "2", LibraryId = _library, MissingSince = DateTime.UtcNow };
        var season = new Season { Title = "Season 1", TmdbId = "3", LibraryId = _library, TvShowId = waiting.Id };
        var elsewhere = new Movie { Title = "Elf", TmdbId = "10719", LibraryId = Guid.NewGuid() };
        await using var db = await SeededAsync(waiting, answered, noTmdb, gone, season, elsewhere);
        var repository = new TitleKeywordRepository(db);

        (await repository.HasTitlesAwaitingKeywordsAsync(_library)).Should().BeTrue();
        var titles = await repository.GetTitlesAwaitingKeywordsAsync(_library, Array.Empty<Guid>(), 10);

        titles.Should().Equal(new Vora.Application.Metadata.TitleAwaitingKeywords(waiting.Id, "18347", true));
        (await repository.GetTitlesAwaitingKeywordsAsync(_library, new[] { waiting.Id }, 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saved_keywords_take_a_title_off_the_waiting_list()
    {
        var community = new TvShow { Title = "Community", TmdbId = "18347", LibraryId = _library };
        await using var db = await SeededAsync(community);
        var repository = new TitleKeywordRepository(db);

        await repository.SaveKeywordsAsync(new Dictionary<Guid, List<string>> { [community.Id] = new() { "halloween episode", "community college" } });

        (await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == community.Id, TestContext.Current.CancellationToken)).Keywords
            .Should().Equal("halloween episode", "community college");
        (await repository.HasTitlesAwaitingKeywordsAsync(_library)).Should().BeFalse();
    }
}
