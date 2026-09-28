using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class SmartListUndatedOrderTests
{
    private readonly Guid _library = Guid.NewGuid();

    private VoraDbContext NewContext()
    {
        var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("smartlist-undated-" + Guid.NewGuid().ToString("N")).Options);
        db.Add(new MediaLibrary { Id = _library, Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } });
        db.AddRange(
            new Movie { Title = "Undated", LibraryId = _library },
            new Movie { Title = "Old", LibraryId = _library, ReleaseDate = new DateOnly(1999, 6, 1), ThirdPartyRating1 = 6.1m },
            new Movie { Title = "New", LibraryId = _library, ReleaseDate = new DateOnly(2026, 3, 20), ThirdPartyRating1 = 8.2m });
        db.SaveChanges();
        return db;
    }

    private async Task<List<string>> TitlesAsync(SmartListSortBy sort)
    {
        await using var db = NewContext();
        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            null, _library, new SmartListRulesDto { MediaTypes = ["Movie"] }, sort, 10);
        return items.Select(i => i.Title).ToList();
    }

    [Fact]
    public async Task Recently_released_never_leads_with_something_that_has_no_date()
    {
        (await TitlesAsync(SmartListSortBy.ReleaseDateDesc)).Should().Equal("New", "Old", "Undated");
    }

    [Fact]
    public async Task Oldest_first_also_puts_undated_last()
    {
        (await TitlesAsync(SmartListSortBy.ReleaseDateAsc)).Should().Equal("Old", "New", "Undated");
    }

    [Fact]
    public async Task Top_rated_puts_unrated_last()
    {
        (await TitlesAsync(SmartListSortBy.TopRated)).Should().Equal("New", "Old", "Undated");
    }
}
