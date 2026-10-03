using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresArtistTagTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task Saved_tags_replace_the_old_ones_and_read_back_most_used_first()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        await using var db = database.NewContext();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Music", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Fall Out Boy", LibraryId = library.Id };
        db.Set<MediaLibrary>().Add(library);
        db.Set<Artist>().Add(artist);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new MusicRepository(db);

        await repository.StageArtistTagsAsync(artist.Id, new[] { "rock", "emo" });
        await repository.SaveMusicChangesAsync(TestContext.Current.CancellationToken);
        await repository.StageArtistTagsAsync(artist.Id, new[] { "pop punk", "rock", "alternative" });
        await repository.SaveMusicChangesAsync(TestContext.Current.CancellationToken);

        (await new MusicRepository(database.NewContext()).GetArtistTagNamesAsync(artist.Id))
            .Should().Equal("pop punk", "rock", "alternative");
    }
}
