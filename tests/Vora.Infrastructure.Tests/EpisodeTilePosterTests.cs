using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class EpisodeTilePosterTests
{
    private readonly Guid _library = Guid.NewGuid();

    private async Task<string?> EpisodePosterAsync(string? showPoster, string? seasonPoster)
    {
        await using var db = new VoraDbContext(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("episode-poster-" + Guid.NewGuid().ToString("N")).Options);
        db.Add(new MediaLibrary { Id = _library, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });
        var show = new TvShow { Title = "MobLand", LibraryId = _library, PosterUrl = showPoster };
        var season = new Season { Title = "Season 2", SeasonNumber = 2, TvShowId = show.Id, LibraryId = _library, PosterUrl = seasonPoster };
        db.AddRange(show, season, new Episode { Title = "I Wanna Be Your Dog", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = _library, ReleaseDate = new DateOnly(2026, 9, 20) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            null, _library, new SmartListRulesDto { MediaTypes = ["Episode"] }, SmartListSortBy.ReleaseDateDesc, 10);
        return items.Single().PosterUrl;
    }

    [Fact]
    public async Task An_episode_tile_shows_the_shows_poster()
    {
        (await EpisodePosterAsync("/art/show.jpg", "/art/season-two.jpg")).Should().Be("/art/show.jpg");
    }

    [Fact]
    public async Task It_falls_back_to_the_seasons_poster_when_the_show_has_none()
    {
        (await EpisodePosterAsync(null, "/art/season-two.jpg")).Should().Be("/art/season-two.jpg");
    }
}
