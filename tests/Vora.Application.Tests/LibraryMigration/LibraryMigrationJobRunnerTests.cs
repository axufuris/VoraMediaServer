using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.LibraryMigration;
using Vora.Application.LibraryMigration.ViewModels;
using Vora.Application.Tasks;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.LibraryMigration;

public class LibraryMigrationJobRunnerTests
{
    private readonly Guid _doubleEpisodeId = Guid.NewGuid();
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly ILibraryMigrationRepository _repository = Substitute.For<ILibraryMigrationRepository>();
    private readonly ITaskQueueManager _taskQueue = Substitute.For<ITaskQueueManager>();
    private readonly ILibrarySyncProvider _plex = Substitute.For<ILibrarySyncProvider>();

    public LibraryMigrationJobRunnerTests()
    {
        _plex.Id.Returns("plex");
        _plex.ResolveUserTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("user-token");

        var showIds = new RemoteExternalIdsDto { TmdbId = "126027" };
        var secondHalf = new RemoteExternalIdsDto { TvdbId = "11401561" };
        _plex.FetchUserDataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RemoteSyncScopeDto>(), Arg.Any<CancellationToken>())
            .Returns(new RemoteUserDataDto
            {
                WatchStates = [new RemoteWatchStateDto { Kind = RemoteMediaKind.Episode, ExternalIds = showIds, EpisodeIds = secondHalf, SeasonNumber = 5, EpisodeNumber = 10, IsPlayed = true }],
                Ratings = [new RemoteRatingDto { Kind = RemoteMediaKind.Episode, ExternalIds = showIds, EpisodeIds = secondHalf, SeasonNumber = 5, EpisodeNumber = 10, Rating = 8m }],
            });

        _repository.FindMatchesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>()).Returns(new List<MediaItemMatchRow>());
        _repository.FindEpisodeMatchesByOwnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>()).Returns(new List<MediaItemMatchRow>());
        _repository.FindEpisodeMatchesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>())
            .Returns(new List<EpisodeMatchRow> { new() { Id = _doubleEpisodeId, ShowTmdbId = "126027", SeasonNumber = 5, EpisodeNumber = 9, EndEpisodeNumber = 10 } });
        _repository.BulkSetAdminRatingsAsync(Arg.Any<IReadOnlyCollection<RatingUpsert>>()).Returns(new List<Guid> { _libraryId });
    }

    private async Task<LibraryMigrationJobVM> RunAsync(bool setAdminRatings)
    {
        var services = new ServiceCollection()
            .AddSingleton(_plex)
            .AddSingleton(_repository)
            .AddSingleton(_taskQueue)
            .AddSingleton(Substitute.For<Vora.Application.Analysis.IClientNotifier>())
            .BuildServiceProvider();
        var runner = new LibraryMigrationJobRunner(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<LibraryMigrationJobRunner>.Instance);

        var job = runner.StartJob(new LibraryMigrationJobInput
        {
            ProviderId = "plex",
            AdminAccessToken = "admin-token",
            ServerClientIdentifier = "server",
            ServerName = "AXMediaServer",
            ConnectionUri = "http://plex.example.com:32400",
            IncludeWatchState = true,
            IncludeRatings = true,
            SetAdminRatings = setAdminRatings,
            LibrarySectionKeys = ["2"],
            Mappings = [new LibraryMigrationMappingInput { AccountId = "1", AccountName = "Andy Xufuris", ProfileId = _profileId, ProfileName = "AX Media Server" }],
        });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var current = runner.GetJob(job.JobId);
            if (current?.State is LibraryMigrationJobState.Completed or LibraryMigrationJobState.Failed) return current;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The migration job did not finish.");
    }

    [Fact]
    public async Task The_second_half_of_a_double_episode_file_lands_on_that_file()
    {
        var job = await RunAsync(setAdminRatings: false);

        job.Users.Single().Skipped.Should().Be(0);
        await _repository.Received(1).BulkUpsertWatchStatesAsync(_profileId, Arg.Is<IReadOnlyCollection<WatchStateUpsert>>(w => w.Single().MediaItemId == _doubleEpisodeId && w.Single().IsPlayed));
        await _repository.Received(1).BulkUpsertRatingsAsync(_profileId, Arg.Is<IReadOnlyCollection<RatingUpsert>>(r => r.Single().MediaItemId == _doubleEpisodeId));
        _taskQueue.DidNotReceive().QueueGenerateLibraryPosterOverlays(Arg.Any<Guid>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Both_halves_of_a_double_episode_count_as_merged_into_one_title_rather_than_missing()
    {
        var showIds = new RemoteExternalIdsDto { TmdbId = "126027" };
        _plex.FetchUserDataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<RemoteSyncScopeDto>(), Arg.Any<CancellationToken>())
            .Returns(new RemoteUserDataDto
            {
                WatchStates =
                [
                    new RemoteWatchStateDto { Kind = RemoteMediaKind.Episode, ExternalIds = showIds, SeasonNumber = 5, EpisodeNumber = 9, IsPlayed = true },
                    new RemoteWatchStateDto { Kind = RemoteMediaKind.Episode, ExternalIds = showIds, SeasonNumber = 5, EpisodeNumber = 10, IsPlayed = true },
                ],
                Ratings = [new RemoteRatingDto { Kind = RemoteMediaKind.Episode, ExternalIds = showIds, SeasonNumber = 5, EpisodeNumber = 9, Rating = 8m }],
            });

        var user = (await RunAsync(setAdminRatings: false)).Users.Single();

        user.WatchStatesFetched.Should().Be(2);
        user.WatchStatesImported.Should().Be(1);
        user.WatchStatesMerged.Should().Be(1);
        user.RatingsImported.Should().Be(1);
        user.RatingsMerged.Should().Be(0);
        user.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task Server_ratings_set_by_the_import_redraw_the_poster_overlays_of_the_libraries_they_changed()
    {
        await RunAsync(setAdminRatings: true);

        _taskQueue.Received(1).QueueGenerateLibraryPosterOverlays(_libraryId, Arg.Any<string?>());
    }
}
