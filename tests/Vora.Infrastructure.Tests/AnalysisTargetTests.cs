using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class AnalysisTargetTests
{
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("analysis-target-tests-" + Guid.NewGuid().ToString("N"))
        .Options);

    private MediaRepository Repo() => new(NullLogger<MediaRepository>.Instance, _db);

    private Guid Library(LibraryType type)
    {
        var id = Guid.NewGuid();
        _db.Set<MediaLibrary>().Add(new MediaLibrary { Id = id, Name = type.ToString(), Type = type, FolderPaths = new List<string> { "/media" } });
        return id;
    }

    private Movie AddMovie(Guid libraryId, string title, DateTime? analyzedAt, DateTime? markersAt = null, DateTime? missingSince = null)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = libraryId, MarkersAnalyzedAt = markersAt, MissingSince = missingSince };
        movie.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = $"/media/{title}.mkv", LastAnalyzedAt = analyzedAt, MediaItemId = movie.Id });
        _db.Set<Movie>().Add(movie);
        return movie;
    }

    private (TvShow Show, Season Season) AddShow(Guid libraryId, string title)
    {
        var show = new TvShow { Id = Guid.NewGuid(), Title = title, LibraryId = libraryId };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = libraryId };
        _db.Set<TvShow>().Add(show);
        _db.Set<Season>().Add(season);
        return (show, season);
    }

    private Episode AddEpisode(Guid libraryId, Season season, int number, DateTime? markersAt, List<string>? locked = null, DateTime? missingSince = null)
    {
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Title = $"Episode {number}",
            LibraryId = libraryId,
            SeasonId = season.Id,
            EpisodeNumber = number,
            MarkersAnalyzedAt = markersAt,
            LockedFields = locked ?? new List<string>(),
            MissingSince = missingSince
        };
        _db.Set<Episode>().Add(episode);
        return episode;
    }

    [Fact]
    public async Task File_analysis_targets_only_items_with_a_part_never_analyzed()
    {
        var movies = Library(LibraryType.Movie);
        var other = Library(LibraryType.Movie);
        var fresh = AddMovie(movies, "Fresh", analyzedAt: null);
        AddMovie(movies, "Done", analyzedAt: DateTime.UtcNow);
        AddMovie(movies, "Gone", analyzedAt: null, missingSince: DateTime.UtcNow);
        AddMovie(other, "Elsewhere", analyzedAt: null);
        _db.SaveChanges();

        (await Repo().GetFileAnalysisTargetIdsAsync(movies)).Should().Equal(fresh.Id);
    }

    [Fact]
    public async Task Marker_detection_targets_movies_without_markers_unless_locked()
    {
        var movies = Library(LibraryType.Movie);
        var pending = AddMovie(movies, "Pending", DateTime.UtcNow);
        AddMovie(movies, "Done", DateTime.UtcNow, markersAt: DateTime.UtcNow);
        var locked = AddMovie(movies, "Locked", DateTime.UtcNow);
        locked.LockedFields = new List<string> { "Markers" };
        _db.SaveChanges();

        (await Repo().GetMarkerDetectionTargetIdsAsync(movies)).Should().Equal(pending.Id);
    }

    [Fact]
    public async Task Marker_detection_targets_a_show_once_when_any_of_its_episodes_needs_markers()
    {
        var shows = Library(LibraryType.TvShow);
        var (pendingShow, pendingSeason) = AddShow(shows, "Titus");
        AddEpisode(shows, pendingSeason, 1, markersAt: DateTime.UtcNow);
        AddEpisode(shows, pendingSeason, 2, markersAt: null);
        AddEpisode(shows, pendingSeason, 3, markersAt: null);

        var (_, doneSeason) = AddShow(shows, "Mythic Quest");
        AddEpisode(shows, doneSeason, 1, markersAt: DateTime.UtcNow);

        var (_, lockedSeason) = AddShow(shows, "Locked Show");
        AddEpisode(shows, lockedSeason, 1, markersAt: null, locked: new List<string> { "Markers" });

        var (_, missingSeason) = AddShow(shows, "Missing Show");
        AddEpisode(shows, missingSeason, 1, markersAt: null, missingSince: DateTime.UtcNow);
        _db.SaveChanges();

        (await Repo().GetMarkerDetectionTargetIdsAsync(shows)).Should().Equal(pendingShow.Id);
    }

    [Fact]
    public async Task A_season_whose_only_unmarked_episode_is_missing_has_no_pending_work()
    {
        var shows = Library(LibraryType.TvShow);
        var (_, season) = AddShow(shows, "Titus");
        AddEpisode(shows, season, 1, markersAt: DateTime.UtcNow);
        AddEpisode(shows, season, 2, markersAt: null, missingSince: DateTime.UtcNow);
        _db.SaveChanges();

        (await Repo().SeasonHasPendingMarkerWorkAsync(season.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Part_file_states_carry_sidecars_but_not_downloaded_subtitles()
    {
        var movies = Library(LibraryType.Movie);
        var movie = AddMovie(movies, "Dead Snow", DateTime.UtcNow);
        var part = movie.MediaParts.Single();
        part.SubtitleTracks.Add(new MediaSubtitleTrack { ExternalFilePath = "/media/Dead Snow.en.srt" });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { ExternalFilePath = "/subtitles/Dead Snow.de.srt", IsDownloaded = true });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { StreamIndex = 3 });
        AddMovie(movies, "Gone", DateTime.UtcNow, missingSince: DateTime.UtcNow);
        _db.SaveChanges();

        var states = await Repo().GetLibraryPartFileStatesAsync(movies);

        states.Should().ContainSingle();
        states[0].PartId.Should().Be(part.Id);
        states[0].ExternalSubtitlePaths.Should().Equal("/media/Dead Snow.en.srt");
    }

    [Fact]
    public async Task A_part_changed_on_disk_is_queued_for_analysis_and_its_sprite_marked_stale()
    {
        var movies = Library(LibraryType.Movie);
        var movie = AddMovie(movies, "Dead Snow", DateTime.UtcNow);
        var part = movie.MediaParts.Single();
        part.VideoThumbnailSpriteVersion = "v2-webp-10-320";
        _db.SaveChanges();

        await Repo().MarkPartsChangedOnDiskAsync(new[] { part.Id });

        var saved = _db.MediaParts.AsNoTracking().Single(p => p.Id == part.Id);
        saved.LastAnalyzedAt.Should().BeNull();
        saved.VideoThumbnailSpriteVersion.Should().BeNull();
        (await Repo().GetFileAnalysisTargetIdsAsync(movies)).Should().Equal(movie.Id);
    }

    [Fact]
    public async Task Movies_analyzed_under_the_old_credits_rules_are_detected_again_once()
    {
        var movies = Library(LibraryType.Movie);
        var before = Vora.Application.Analysis.MarkerAssembler.MovieCreditsRulesChangedAt.AddDays(-1);
        var after = Vora.Application.Analysis.MarkerAssembler.MovieCreditsRulesChangedAt.AddHours(1);
        var stale = AddMovie(movies, "Spider-Man", analyzedAt: before, markersAt: before);
        stale.HasPostCreditsStinger = true;
        var redone = AddMovie(movies, "Thor", analyzedAt: after, markersAt: after);
        redone.HasMidCreditsStinger = true;
        var noStinger = AddMovie(movies, "Jurassic World", analyzedAt: before, markersAt: before);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repo().GetMarkerDetectionTargetIdsAsync(movies)).Should().BeEquivalentTo(new[] { stale.Id, noStinger.Id });
        (await Repo().GetMarkerDetectionGateAsync(stale.Id))?.MarkersPredateMovieCreditsRules.Should().BeTrue();
        (await Repo().GetMarkerDetectionGateAsync(noStinger.Id))?.MarkersPredateMovieCreditsRules.Should().BeTrue();
        (await Repo().GetMarkerDetectionGateAsync(redone.Id))?.MarkersPredateMovieCreditsRules.Should().BeFalse();
    }
}
