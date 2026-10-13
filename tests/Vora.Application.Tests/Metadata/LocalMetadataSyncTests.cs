using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Metadata;

public class LocalMetadataSyncTests
{
    private readonly Guid _library = Guid.NewGuid();
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly IMetadataFetchService _fetch = Substitute.For<IMetadataFetchService>();
    private readonly IMetadataMappingService _mapping = Substitute.For<IMetadataMappingService>();
    private readonly ILocalMetadataChangeFinder _changes = Substitute.For<ILocalMetadataChangeFinder>();
    private readonly IMetadataManager _scoped = Substitute.For<IMetadataManager>();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();
    private readonly MetadataManager _manager;

    public LocalMetadataSyncTests()
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMetadataManager)).Returns(_scoped);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        _scopes.CreateScope().Returns(scope);

        _media.GetDisplayTitlesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new Dictionary<Guid, string>());

        _manager = new MetadataManager(
            _media, Substitute.For<IActorRepository>(), _scopes, _notifier, _fetch, _mapping,
            Substitute.For<Vora.Application.Settings.ISystemSettingsRepository>(), Array.Empty<Vora.Plugins.Interfaces.IMetadataProvider>(),
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(), _changes, NullLogger<MetadataManager>.Instance);
    }

    private void Found(IReadOnlyList<LocalMediaSourceDto> documents, params Guid[] artwork) =>
        _changes.FindAsync(_library, Arg.Any<DateTime?>(), Arg.Any<CancellationToken>()).Returns(new LocalMetadataChanges(documents, artwork));

    private static LocalMediaSourceDto Source(string kind, Guid? showId = null) =>
        new(Guid.NewGuid(), kind, null, null, []) { TvShowId = showId };

    private void Identity(Guid id, params string[] tmdbIds) =>
        _media.GetProjectedAsync(id, Arg.Any<Expression<Func<MediaItem, ExternalIdSet>>>())
            .Returns(ExternalIdSet.From(tmdbIds[0]), tmdbIds.Skip(1).Select(tmdbId => ExternalIdSet.From(tmdbId)).ToArray());

    [Fact]
    public async Task Nothing_changed_means_nothing_is_touched()
    {
        Found([]);

        var artwork = await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        artwork.Should().BeEmpty();
        await _media.DidNotReceive().ClearMetadataRefreshAsync(Arg.Any<IReadOnlyCollection<Guid>>());
        _scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task A_film_whose_nfo_changed_is_refreshed_without_overriding_locked_fields()
    {
        var movie = Source(LocalMediaSourceDto.Movie);
        Found([movie]);

        await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        await _media.Received(1).ClearMetadataRefreshAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == movie.Id));
        await _scoped.Received(1).RefreshMetadataAsync(movie.Id, false, true);
    }

    [Fact]
    public async Task Edited_episodes_of_one_show_are_refreshed_together_and_the_rest_of_the_show_is_left_alone()
    {
        var showId = Guid.NewGuid();
        var first = Source(LocalMediaSourceDto.Episode, showId);
        var second = Source(LocalMediaSourceDto.Episode, showId);
        Found([first, second]);

        await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        _scopes.Received(1).CreateScope();
        await _scoped.Received(1).RefreshMetadataAsync(first.Id, false, false);
        await _scoped.Received(1).RefreshMetadataAsync(second.Id, false, false);
        await _scoped.DidNotReceive().RefreshMetadataAsync(showId, Arg.Any<bool>(), Arg.Any<bool>());
        await _scoped.DidNotReceive().TriggerMediaItemMetadataRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyMediaItemUpdatedAsync(showId);
    }

    [Fact]
    public async Task A_show_that_the_nfo_now_names_differently_has_every_episode_read_again()
    {
        var showId = Guid.NewGuid();
        var show = new LocalMediaSourceDto(showId, LocalMediaSourceDto.TvShow, null, null, []) { TvShowId = showId };
        var episodes = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        Found([show]);
        Identity(showId, "9", "124364");
        _media.GetEpisodeIdsForShowAsync(showId).Returns(episodes);

        await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _scoped.RefreshMetadataAsync(showId, false, true);
            _media.ClearMetadataRefreshAsync(episodes);
            _scoped.TriggerMediaItemMetadataRefreshAsync(showId, false, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_show_whose_identity_stays_the_same_does_not_drag_its_episodes_along()
    {
        var showId = Guid.NewGuid();
        var show = new LocalMediaSourceDto(showId, LocalMediaSourceDto.TvShow, null, null, []) { TvShowId = showId };
        Found([show]);
        Identity(showId, "124364", "124364");

        await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        await _scoped.Received(1).RefreshMetadataAsync(showId, false, true);
        await _scoped.DidNotReceive().TriggerMediaItemMetadataRefreshAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _media.DidNotReceive().GetEpisodeIdsForShowAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Titles_whose_local_images_changed_have_their_artwork_fetched_again_and_are_reported_back()
    {
        var movie = new Movie { Title = "Alien", Library = new MediaLibrary { Name = "Movies" } };
        Found([], movie.Id);
        _media.GetForMetadataSyncAsync(movie.Id).Returns(movie);
        _fetch.GetArtworkAsync(movie).Returns(new List<MediaArtwork>());

        var artwork = await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, null, TestContext.Current.CancellationToken);

        artwork.Should().Equal(movie.Id);
        await _fetch.Received(1).GetArtworkAsync(movie);
        await _mapping.Received(1).ApplyArtworkAsync(movie, Arg.Any<List<MediaArtwork>>(), false);
    }

    [Fact]
    public async Task The_time_the_setting_changed_is_passed_on_so_a_restart_does_not_redo_finished_titles()
    {
        var since = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
        Found([]);

        await _manager.TriggerLibraryLocalMetadataSyncAsync(_library, since, TestContext.Current.CancellationToken);

        await _changes.Received(1).FindAsync(_library, since, Arg.Any<CancellationToken>());
    }
}
