using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresStorageReferenceTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private const string ArtworkUrl = "/api/artwork/custom/";
    private const string ArtworkFolder = "/app/data/custom_artwork/";
    private const string ProfileUrl = "/api/users/images/custom/";

    [Fact]
    public async Task Every_text_json_and_array_column_is_searched_for_file_references()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Movie>().Add(new Movie
        {
            Id = Guid.NewGuid(),
            Title = "Heat",
            LibraryId = library.Id,
            PosterUrl = ArtworkUrl + "media_a_poster_1.jpg?v=3",
            BackgroundUrl = "https://image.tmdb.org/t/p/w1280/heat.jpg",
            Overview = $"Moved from {ArtworkFolder}music_poster_b.jpg last week.",
            LockedFields = new List<string> { "Markers", ArtworkUrl + "coll_c_poster_2.png" }
        });
        var settings = await db.Set<ServerSetting>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        if (settings == null)
        {
            settings = new ServerSetting { Id = "GLOBAL_SETTINGS" };
            db.Set<ServerSetting>().Add(settings);
        }
        settings.BackupConfigurationJson = $"{{\"Cover\":\"{ProfileUrl}profile_d.png\",\"Other\":\"{ArtworkUrl}media_e_logo_3.png\"}}";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await new StorageReferenceRepository(database.NewContext())
            .FindReferencedNamesAsync(new[] { ArtworkUrl, ArtworkFolder, ProfileUrl }, TestContext.Current.CancellationToken);

        found[ArtworkUrl].Should().Contain(new[] { "media_a_poster_1.jpg", "coll_c_poster_2.png", "media_e_logo_3.png" });
        found[ArtworkUrl].Should().NotContain(name => name.Contains('?'));
        found[ArtworkFolder].Should().Contain("music_poster_b.jpg");
        found[ProfileUrl].Should().Contain("profile_d.png");
    }
}
