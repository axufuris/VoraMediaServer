using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryAnalysisTargetsTests
{
    private static readonly DateTime Analyzed = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("analysis-targets-" + Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task FileAnalysisTargets_returns_only_items_with_an_unanalyzed_part()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var analyzed = new Movie { Id = Guid.NewGuid(), Title = "Analyzed", LibraryId = libraryId };
        analyzed.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = "/a.mkv", LastAnalyzedAt = Analyzed });
        var pending = new Movie { Id = Guid.NewGuid(), Title = "Pending", LibraryId = libraryId };
        pending.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = "/b.mkv" });
        db.Set<Movie>().AddRange(analyzed, pending);
        await db.SaveChangesAsync();

        var ids = await repo.GetFileAnalysisTargetIdsAsync(libraryId);

        Assert.Equal(new[] { pending.Id }, ids);
    }

    [Fact]
    public async Task MarkerDetectionTargets_non_forced_returns_movies_and_shows_with_unanalyzed_markers()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var doneMovie = new Movie { Id = Guid.NewGuid(), Title = "DoneMovie", LibraryId = libraryId, MarkersAnalyzedAt = Analyzed };
        var pendingMovie = new Movie { Id = Guid.NewGuid(), Title = "PendingMovie", LibraryId = libraryId };

        var doneShow = new TvShow { Id = Guid.NewGuid(), Title = "DoneShow", LibraryId = libraryId };
        var doneSeason = new Season { Id = Guid.NewGuid(), Title = "S1", SeasonNumber = 1, TvShowId = doneShow.Id, TvShow = doneShow, LibraryId = libraryId };
        doneSeason.Episodes.Add(new Episode { Id = Guid.NewGuid(), Title = "E1", EpisodeNumber = 1, SeasonId = doneSeason.Id, Season = doneSeason, LibraryId = libraryId, MarkersAnalyzedAt = Analyzed });

        var pendingShow = new TvShow { Id = Guid.NewGuid(), Title = "PendingShow", LibraryId = libraryId };
        var pendingSeason = new Season { Id = Guid.NewGuid(), Title = "S1", SeasonNumber = 1, TvShowId = pendingShow.Id, TvShow = pendingShow, LibraryId = libraryId };
        pendingSeason.Episodes.Add(new Episode { Id = Guid.NewGuid(), Title = "E1", EpisodeNumber = 1, SeasonId = pendingSeason.Id, Season = pendingSeason, LibraryId = libraryId, MarkersAnalyzedAt = Analyzed });
        pendingSeason.Episodes.Add(new Episode { Id = Guid.NewGuid(), Title = "E2", EpisodeNumber = 2, SeasonId = pendingSeason.Id, Season = pendingSeason, LibraryId = libraryId });

        db.Set<Movie>().AddRange(doneMovie, pendingMovie);
        db.Set<TvShow>().AddRange(doneShow, pendingShow);
        db.Set<Season>().AddRange(doneSeason, pendingSeason);
        await db.SaveChangesAsync();

        var remaining = await repo.GetMarkerDetectionTargetIdsAsync(libraryId, includeCompleted: false);
        Assert.Equal(new[] { pendingMovie.Id, pendingShow.Id }.OrderBy(x => x), remaining.OrderBy(x => x));

        var all = await repo.GetMarkerDetectionTargetIdsAsync(libraryId, includeCompleted: true);
        Assert.Equal(4, all.Count); // 2 movies + 2 shows (episodes/seasons excluded)
    }
}
