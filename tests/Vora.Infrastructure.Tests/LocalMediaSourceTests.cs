using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class LocalMediaSourceTests
{
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("local-media-source-" + Guid.NewGuid().ToString("N"))
        .Options);
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly MediaRepository _repository;

    public LocalMediaSourceTests()
    {
        _repository = new MediaRepository(NullLogger<MediaRepository>.Instance, _db);
        _db.Add(new MediaLibrary { Id = _libraryId, Name = "Library", Type = LibraryType.TvShow, FolderPaths = ["/media"] });
    }

    private Movie AddMovie(string title, params string[] files)
    {
        var movie = new Movie { Title = title, LibraryId = _libraryId };
        _db.Add(movie);
        foreach (var file in files) _db.Add(new MediaPart { FilePath = file, MediaItemId = movie.Id });
        return movie;
    }

    private (TvShow Show, Season Season, List<Episode> Episodes) AddShow(string title, int seasonNumber, params string[] files)
    {
        var show = new TvShow { Title = title, LibraryId = _libraryId };
        var season = new Season { Title = $"Season {seasonNumber}", SeasonNumber = seasonNumber, TvShowId = show.Id, LibraryId = _libraryId };
        _db.AddRange(show, season);
        var episodes = new List<Episode>();
        for (var index = 0; index < files.Length; index++)
        {
            var episode = new Episode { Title = $"Episode {index + 1}", EpisodeNumber = index + 1, SeasonId = season.Id, LibraryId = _libraryId };
            _db.Add(episode);
            _db.Add(new MediaPart { FilePath = files[index], MediaItemId = episode.Id });
            episodes.Add(episode);
        }
        return (show, season, episodes);
    }

    [Fact]
    public async Task A_film_is_found_through_its_own_files()
    {
        var movie = AddMovie("Alien", "/media/Alien (1979)/Alien (1979) - 1080p.mkv", "/media/Alien (1979)/Alien (1979) - 4K.mkv");
        movie.OriginalPosterUrl = "https://image.tmdb.org/t/p/w500/alien.jpg";
        movie.LockField(nameof(movie.PosterUrl));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var source = Assert.IsType<LocalMediaSourceDto>(await _repository.GetLocalMediaSourceAsync(movie.Id));

        source.Kind.Should().Be(LocalMediaSourceDto.Movie);
        source.FilePaths.Should().BeEquivalentTo("/media/Alien (1979)/Alien (1979) - 1080p.mkv", "/media/Alien (1979)/Alien (1979) - 4K.mkv");
        source.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/alien.jpg");
        source.IsLocked("posterurl").Should().BeTrue();
    }

    [Fact]
    public async Task A_show_a_season_and_an_episode_are_each_found_through_the_episode_files()
    {
        var (show, season, episodes) = AddShow("From", 2, "/media/From/Season 02/From - S02E01.mkv", "/media/From/Season 02/From - S02E02.mkv");
        AddShow("Another show", 1, "/media/Another/Season 01/Another - S01E01.mkv");
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var forShow = Assert.IsType<LocalMediaSourceDto>(await _repository.GetLocalMediaSourceAsync(show.Id));
        var forSeason = Assert.IsType<LocalMediaSourceDto>(await _repository.GetLocalMediaSourceAsync(season.Id));
        var forEpisode = Assert.IsType<LocalMediaSourceDto>(await _repository.GetLocalMediaSourceAsync(episodes[1].Id));

        forShow.Kind.Should().Be(LocalMediaSourceDto.TvShow);
        forShow.FilePaths.Should().HaveCount(2).And.OnlyContain(path => path.StartsWith("/media/From/"));
        forSeason.Kind.Should().Be(LocalMediaSourceDto.Season);
        forSeason.SeasonNumber.Should().Be(2);
        forSeason.TvShowId.Should().Be(show.Id);
        forSeason.FilePaths.Should().HaveCount(2);
        forEpisode.Kind.Should().Be(LocalMediaSourceDto.Episode);
        forEpisode.SeasonNumber.Should().Be(2);
        forEpisode.EpisodeNumber.Should().Be(2);
        forEpisode.TvShowId.Should().Be(show.Id);
        forEpisode.FilePaths.Should().Equal("/media/From/Season 02/From - S02E02.mkv");
    }

    [Fact]
    public async Task Something_that_is_not_a_film_or_part_of_a_show_has_no_place_on_disk_to_read()
    {
        var track = new Track { Title = "Song", LibraryId = _libraryId };
        _db.Add(track);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetLocalMediaSourceAsync(track.Id)).Should().BeNull();
        (await _repository.GetLocalMediaSourceAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task A_whole_library_is_listed_once_with_each_title_tied_to_its_files()
    {
        var movie = AddMovie("Alien", "/media/Alien (1979)/Alien (1979).mkv");
        var missing = AddMovie("Gone", "/media/Gone (2000)/Gone (2000).mkv");
        missing.MissingSince = DateTime.UtcNow;
        var (show, season, episodes) = AddShow("From", 1, "/media/From/Season 01/From - S01E01.mkv", "/media/From/Season 01/From - S01E02.mkv");
        _db.Add(new Track { Title = "Song", LibraryId = _libraryId });
        _db.Add(new Movie { Title = "In another library", LibraryId = Guid.NewGuid() });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sources = await _repository.GetLibraryLocalMediaSourcesAsync(_libraryId);

        sources.Select(source => source.Id).Should().BeEquivalentTo(new[] { movie.Id, show.Id, season.Id, episodes[0].Id, episodes[1].Id });
        sources.Single(source => source.Id == movie.Id).FilePaths.Should().Equal("/media/Alien (1979)/Alien (1979).mkv");
        sources.Single(source => source.Id == show.Id).FilePaths.Should().HaveCount(2);
        sources.Single(source => source.Id == show.Id).TvShowId.Should().Be(show.Id);
        sources.Single(source => source.Id == season.Id).FilePaths.Should().HaveCount(2);
        sources.Single(source => source.Id == season.Id).SeasonNumber.Should().Be(1);
        var second = sources.Single(source => source.Id == episodes[1].Id);
        second.Kind.Should().Be(LocalMediaSourceDto.Episode);
        second.EpisodeNumber.Should().Be(2);
        second.SeasonNumber.Should().Be(1);
        second.TvShowId.Should().Be(show.Id);
        second.FilePaths.Should().Equal("/media/From/Season 01/From - S01E02.mkv");
    }

    [Fact]
    public async Task The_owner_of_a_film_file_is_the_film_and_says_whether_it_was_matched_by_hand()
    {
        var matched = AddMovie("Alien", "/media/Alien (1979)/Alien (1979).mkv");
        matched.LockField(MediaMatchManager.MatchLock);
        var automatic = AddMovie("Heat", "/media/Heat (1995)/Heat (1995).mkv");
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetTitleOwningFileAsync(_libraryId, "/media/Alien (1979)/Alien (1979).mkv")).Should().Be(new TitleOwnerDto(matched.Id, true));
        (await _repository.GetTitleOwningFileAsync(_libraryId, "/media/Heat (1995)/Heat (1995).mkv")).Should().Be(new TitleOwnerDto(automatic.Id, false));
        (await _repository.GetTitleOwningFileAsync(_libraryId, "/media/Unknown.mkv")).Should().BeNull();
        (await _repository.GetTitleOwningFileAsync(Guid.NewGuid(), "/media/Alien (1979)/Alien (1979).mkv")).Should().BeNull();
    }

    [Fact]
    public async Task The_owner_of_an_episode_file_is_its_show()
    {
        var (show, _, _) = AddShow("From", 1, "/media/From/Season 01/From - S01E01.mkv");
        show.LockField(MediaMatchManager.MatchLock);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await _repository.GetTitleOwningFileAsync(_libraryId, "/media/From/Season 01/From - S01E01.mkv")).Should().Be(new TitleOwnerDto(show.Id, true));
    }

    [Fact]
    public async Task Merging_two_copies_of_a_show_keeps_the_mark_that_one_was_matched_by_hand()
    {
        var (kept, _, _) = AddShow("From", 1, "/media/HD/From/Season 01/From - S01E01.mkv", "/media/HD/From/Season 01/From - S01E02.mkv");
        var (matched, _, _) = AddShow("From", 1, "/media/4K/From/Season 01/From - S01E01.mkv");
        kept.TmdbId = matched.TmdbId = "124364";
        matched.LockField(MediaMatchManager.MatchLock);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new MediaDedupeRepository(_db).MergeDuplicateTvShowsAsync(_libraryId);

        result.ShowsRemoved.Should().Be(1);
        var survivor = await _db.Set<TvShow>().SingleAsync(TestContext.Current.CancellationToken);
        survivor.Id.Should().Be(kept.Id);
        survivor.IsLocked(MediaMatchManager.MatchLock).Should().BeTrue();
    }
}
