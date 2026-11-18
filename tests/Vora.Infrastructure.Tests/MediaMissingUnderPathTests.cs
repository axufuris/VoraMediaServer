using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public sealed class MediaMissingUnderPathTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vora-under-").FullName;
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("missing-under-" + Guid.NewGuid().ToString("N"))
        .Options);

    private readonly MediaLibrary _library = new() { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private MediaRepository Repo() => new(NullLogger<MediaRepository>.Instance, _db);

    private Episode Episode(string folder, string file, bool onDisk, int number)
    {
        var show = new TvShow { Id = Guid.NewGuid(), Title = folder + number, LibraryId = _library.Id, AddedAt = DateTime.UtcNow };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = _library.Id, AddedAt = show.AddedAt };
        var episode = new Episode { Id = Guid.NewGuid(), Title = "Episode " + number, EpisodeNumber = number, SeasonId = season.Id, LibraryId = _library.Id, AddedAt = show.AddedAt };

        var path = Path.Combine(_root, folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (onDisk) File.WriteAllText(path, "x");
        episode.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = path, MediaItemId = episode.Id });

        _db.AddRange(show, season, episode);
        return episode;
    }

    private async Task<DateTime?> MissingSinceAsync(Episode episode)
    {
        _db.ChangeTracker.Clear();
        return (await _db.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == episode.Id, TestContext.Current.CancellationToken)).MissingSince;
    }

    [Fact]
    public async Task Only_the_files_that_are_really_gone_from_under_the_folder_go_to_the_trash()
    {
        _db.Add(_library);
        var present = Episode("Show", "e1.mkv", onDisk: true, number: 1);
        var goneOne = Episode("Show", "e2.mkv", onDisk: false, number: 2);
        var goneTwo = Episode("Show", Path.Combine("Season 2", "e3.mkv"), onDisk: false, number: 3);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trashed = await Repo().MarkMediaMissingUnderPathAsync(Path.Combine(_root, "Show"));

        trashed.Should().Be(2);
        (await MissingSinceAsync(present)).Should().BeNull();
        (await MissingSinceAsync(goneOne)).Should().NotBeNull();
        (await MissingSinceAsync(goneTwo)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_folder_that_only_shares_a_name_prefix_is_left_alone()
    {
        _db.Add(_library);
        var neighbour = Episode("Show Two", "e1.mkv", onDisk: false, number: 1);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trashed = await Repo().MarkMediaMissingUnderPathAsync(Path.Combine(_root, "Show"));

        trashed.Should().Be(0);
        (await MissingSinceAsync(neighbour)).Should().BeNull();
    }

    [Fact]
    public async Task A_trailing_separator_on_the_folder_makes_no_difference()
    {
        _db.Add(_library);
        var gone = Episode("Show", "e1.mkv", onDisk: false, number: 1);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trashed = await Repo().MarkMediaMissingUnderPathAsync(Path.Combine(_root, "Show") + Path.DirectorySeparatorChar);

        trashed.Should().Be(1);
        (await MissingSinceAsync(gone)).Should().NotBeNull();
    }

    [Fact]
    public async Task The_repository_knows_whether_anything_is_kept_under_a_folder()
    {
        _db.Add(_library);
        Episode("Show", "e1.mkv", onDisk: false, number: 1);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repo().AnyMediaUnderPathAsync(Path.Combine(_root, "Show"))).Should().BeTrue();
        (await Repo().AnyMediaUnderPathAsync(Path.Combine(_root, "Sho"))).Should().BeFalse();
        (await Repo().AnyMediaUnderPathAsync(Path.Combine(_root, "Other"))).Should().BeFalse();
    }
}
