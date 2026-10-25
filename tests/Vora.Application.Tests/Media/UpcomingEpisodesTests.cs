using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Media.ViewModels;
using Vora.Application.Settings;
using Vora.Application.Subtitles;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

public class UpcomingEpisodesTests
{
    private static string Upcoming(params (int Season, int Episode, string Title, string AirDate)[] episodes) =>
        JsonSerializer.Serialize(episodes.Select(e => new UpcomingEpisodeDto
        {
            SeasonNumber = e.Season,
            EpisodeNumber = e.Episode,
            Title = e.Title,
            AirDate = DateOnly.Parse(e.AirDate),
        }).ToList());

    private static List<(int Season, int Episode)> Left(string? json) =>
        (JsonSerializer.Deserialize<List<UpcomingEpisodeDto>>(json ?? "[]") ?? [])
            .Select(e => (e.SeasonNumber, e.EpisodeNumber)).ToList();

    [Fact]
    public void An_episode_the_library_holds_is_not_the_next_one()
    {
        var json = Upcoming((2, 1, "ROMULUS S.V.", "2026-10-21"));

        UpcomingEpisodes.WithoutHeld(json, [new HeldEpisode(2, 1, null)]).Should().Be("[]");
    }

    [Fact]
    public void The_next_one_is_the_first_the_library_does_not_hold()
    {
        var json = Upcoming((2, 5, "Frogman", "2026-10-21"), (2, 6, "Rats", "2026-10-28"), (2, 7, "Seven", "2026-11-04"));

        var left = UpcomingEpisodes.WithoutHeld(json, [new HeldEpisode(2, 4, null), new HeldEpisode(2, 5, null)]);

        Left(left).Should().Equal((2, 6), (2, 7));
    }

    [Fact]
    public void A_file_holding_two_episodes_holds_both()
    {
        var json = Upcoming((1, 3, "Part One", "2026-11-01"), (1, 4, "Part Two", "2026-11-01"), (1, 5, "After", "2026-11-08"));

        Left(UpcomingEpisodes.WithoutHeld(json, [new HeldEpisode(1, 3, 4)])).Should().Equal((1, 5));
    }

    [Fact]
    public void The_same_episode_number_in_another_season_is_not_held()
    {
        var json = Upcoming((3, 1, "New Season", "2027-01-05"));

        UpcomingEpisodes.WithoutHeld(json, [new HeldEpisode(2, 1, null)]).Should().Be(json);
    }

    [Fact]
    public void Leaves_the_list_as_it_was_when_nothing_changes_or_it_cannot_be_read()
    {
        var json = Upcoming((2, 6, "Rats", "2026-10-28"));

        UpcomingEpisodes.WithoutHeld(json, []).Should().Be(json);
        UpcomingEpisodes.WithoutHeld("not json", [new HeldEpisode(2, 6, null)]).Should().Be("not json");
        UpcomingEpisodes.WithoutHeld(null, [new HeldEpisode(2, 6, null)]).Should().BeNull();
    }

    [Fact]
    public void Writes_the_shorter_list_in_the_shape_the_clients_read()
    {
        var json = Upcoming((2, 5, "Frogman", "2026-10-21"), (2, 6, "Rats", "2026-10-28"));

        var left = UpcomingEpisodes.WithoutHeld(json, [new HeldEpisode(2, 5, null)]) ?? string.Empty;

        left.Should().Contain("\"SeasonNumber\":2").And.Contain("\"EpisodeNumber\":6").And.Contain("\"AirDate\":\"2026-10-28\"");
    }

    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();

    private MediaManager Manager() => new(
        _repository,
        Substitute.For<IUserMediaStateRepository>(),
        Substitute.For<IClientNotifier>(),
        Substitute.For<ITaskQueueManager>(),
        Substitute.For<ISystemSettingsRepository>(),
        [],
        Options.Create(new StoragePathsOptions()),
        Substitute.For<IVideoThumbnailStorageService>(),
        Substitute.For<IArtworkThumbnailService>(),
        Substitute.For<ISubtitlePreExtractionManager>(),
        NullLogger<MediaManager>.Instance);

    [Fact]
    public async Task A_show_page_leaves_out_the_upcoming_episodes_the_library_holds()
    {
        var show = new MediaDetailsVM { Id = Guid.NewGuid(), Title = "The Terminal List", Type = "TvShow", UpcomingEpisodesJson = Upcoming((2, 1, "ROMULUS S.V.", "2026-10-21")) };
        _repository.GetProjectedAsync(show.Id, Arg.Any<Expression<Func<MediaItem, MediaDetailsVM>>>(),
                Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>())
            .Returns(show);
        _repository.GetHeldEpisodesForShowAsync(show.Id).Returns([new HeldEpisode(2, 1, null)]);

        var shown = await Manager().GetMediaItemAsync(show.Id);

        shown?.UpcomingEpisodesJson.Should().Be("[]");
    }

    [Fact]
    public async Task A_season_page_asks_its_show_which_episodes_are_held()
    {
        var showId = Guid.NewGuid();
        var season = new SeasonDetailsVM { Id = Guid.NewGuid(), TvShowId = showId, UpcomingEpisodesJson = Upcoming((2, 1, "ROMULUS S.V.", "2026-10-21"), (2, 9, "Finale", "2026-11-18")) };
        _repository.GetProjectedAsync(season.Id, Arg.Any<Expression<Func<MediaItem, SeasonDetailsVM>>>(),
                Arg.Any<bool>(), Arg.Any<List<Guid>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<bool>())
            .Returns(season);
        _repository.GetHeldEpisodesForShowAsync(showId).Returns([new HeldEpisode(2, 1, null)]);

        var shown = await Manager().GetSeasonDetailsAsync(season.Id);

        Left(shown?.UpcomingEpisodesJson).Should().Equal((2, 9));
    }
}
