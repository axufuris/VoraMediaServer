using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Posters;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Tasks;

public class LibraryRatingsTaskTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>(), Substitute.For<ITaskJournal>());
    private readonly ILibraryRepository _libraries = Substitute.For<ILibraryRepository>();
    private readonly IMusicPopularityRefresher _popularity = Substitute.For<IMusicPopularityRefresher>();
    private readonly IMetadataManager _metadata = Substitute.For<IMetadataManager>();
    private readonly IPosterOverlayManager _overlays = Substitute.For<IPosterOverlayManager>();

    private async Task RunNextAsync()
    {
        var sp = new ServiceCollection()
            .AddSingleton(_libraries).AddSingleton(_popularity).AddSingleton(_metadata).AddSingleton(_overlays)
            .BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            await task.WorkItem(CancellationToken.None, sp);
            return;
        }
    }

    private void LibraryIs(Guid libraryId, LibraryType type) =>
        _libraries.GetProjectedByIdAsync(libraryId, Arg.Any<Expression<Func<MediaLibrary, LibraryType?>>>()).Returns(type);

    [Fact]
    public async Task Refreshing_a_music_librarys_ratings_refreshes_its_artists_popularity_instead()
    {
        var music = Guid.NewGuid();
        LibraryIs(music, LibraryType.Music);
        _queue.QueueRefreshLibraryRatings(music, forceOverride: true);

        await RunNextAsync();

        await _popularity.Received(1).RefreshLibraryArtistsAsync(music, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _metadata.DidNotReceive().TriggerLibraryRatingsRefreshAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await _overlays.DidNotReceive().RunLibraryOverlaySyncAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refreshing_a_movie_librarys_ratings_still_asks_the_rating_providers()
    {
        var movies = Guid.NewGuid();
        LibraryIs(movies, LibraryType.Movie);
        _queue.QueueRefreshLibraryRatings(movies, forceOverride: true);

        await RunNextAsync();

        await _metadata.Received(1).TriggerLibraryRatingsRefreshAsync(movies, null, true, Arg.Is<DateTime?>(since => since != null), Arg.Any<CancellationToken>());
        await _popularity.DidNotReceive().RefreshLibraryArtistsAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
