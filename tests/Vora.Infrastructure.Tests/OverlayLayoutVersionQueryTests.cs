using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class OverlayLayoutVersionQueryTests
{
    private const int Current = 11;

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("overlay-layout-version-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<VoraDbContext> SeedAsync(params (DateTime? Overlaid, int? Layout)[] movies)
    {
        var db = NewContext();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        db.Add(library);
        foreach (var (overlaid, layout) in movies)
        {
            db.Add(new Movie { Id = Guid.NewGuid(), Title = "Tiny Fugitives", LibraryId = library.Id, LastOverlayGeneratedAt = overlaid, OverlayLayoutVersion = layout });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task A_poster_drawn_with_an_older_layout_needs_redrawing()
    {
        await using var db = await SeedAsync((DateTime.UtcNow, Current), (DateTime.UtcNow, 10));

        (await new MediaRepository(NullLogger<MediaRepository>.Instance, db).AnyOverlayFromAnotherLayoutAsync(Current)).Should().BeTrue();
    }

    [Fact]
    public async Task A_poster_overlaid_before_layouts_were_recorded_needs_redrawing()
    {
        await using var db = await SeedAsync((DateTime.UtcNow, null));

        (await new MediaRepository(NullLogger<MediaRepository>.Instance, db).AnyOverlayFromAnotherLayoutAsync(Current)).Should().BeTrue();
    }

    [Fact]
    public async Task Posters_never_overlaid_or_already_current_need_nothing()
    {
        await using var db = await SeedAsync((null, null), (null, 10), (DateTime.UtcNow, Current));

        (await new MediaRepository(NullLogger<MediaRepository>.Instance, db).AnyOverlayFromAnotherLayoutAsync(Current)).Should().BeFalse();
    }
}
