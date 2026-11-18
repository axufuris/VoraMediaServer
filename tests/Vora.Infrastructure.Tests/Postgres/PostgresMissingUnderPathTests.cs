using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

public class PostgresMissingUnderPathTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_folder_sweep_matches_the_folder_literally_and_trashes_only_files_that_are_gone()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("vora-pg-under-").FullName;
        try
        {
            var library = new MediaLibrary { Name = "Shows " + Guid.NewGuid().ToString("N")[..8], Type = LibraryType.TvShow, FolderPaths = new List<string> { root } };
            var show = new TvShow { Title = "Odd_Name 100% (2020)", LibraryId = library.Id, AddedAt = DateTime.UtcNow };
            var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id, AddedAt = show.AddedAt };

            Episode Make(string folder, string file, bool onDisk, int number)
            {
                var path = Path.Combine(root, folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (onDisk) File.WriteAllText(path, "x");
                var episode = new Episode { Title = "E" + number, EpisodeNumber = number, SeasonId = season.Id, LibraryId = library.Id, AddedAt = show.AddedAt };
                episode.MediaParts.Add(new MediaPart { FilePath = path, MediaItemId = episode.Id });
                return episode;
            }

            var present = Make("Odd_Name 100% (2020)", "e1.mkv", onDisk: true, number: 1);
            var gone = Make("Odd_Name 100% (2020)", "e2.mkv", onDisk: false, number: 2);
            var wildcardNeighbour = Make("OddXName 100X (2020)", "e1.mkv", onDisk: false, number: 3);
            var prefixNeighbour = Make("Odd_Name 100% (2020) Extended", "e1.mkv", onDisk: false, number: 4);

            await using (var seed = database.NewContext())
            {
                seed.AddRange(library, show, season, present, gone, wildcardNeighbour, prefixNeighbour);
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var db = database.NewContext();
            var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
            var folder = Path.Combine(root, "Odd_Name 100% (2020)");

            (await repo.AnyMediaUnderPathAsync(folder)).Should().BeTrue();
            (await repo.AnyMediaUnderPathAsync(Path.Combine(root, "Odd"))).Should().BeFalse();
            var trashed = await repo.MarkMediaMissingUnderPathAsync(folder);

            trashed.Should().Be(1);
            await using var check = database.NewContext();
            (await check.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == present.Id, cancellationToken)).MissingSince.Should().BeNull();
            (await check.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == gone.Id, cancellationToken)).MissingSince.Should().NotBeNull();
            (await check.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == wildcardNeighbour.Id, cancellationToken)).MissingSince.Should().BeNull();
            (await check.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == prefixNeighbour.Id, cancellationToken)).MissingSince.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
