using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Metadata;

public class ReplaceAllMetadataTests
{
    private static readonly DateTime Clicked = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly MediaLibrary _library = new() { Name = "Shows", Type = LibraryType.TvShow };
    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly IMetadataFetchService _fetch = Substitute.For<IMetadataFetchService>();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();
    private readonly MetadataManager _manager;

    public ReplaceAllMetadataTests()
    {
        _manager = new MetadataManager(
            _media, Substitute.For<IActorRepository>(), _scopes, _notifier, _fetch, Substitute.For<IMetadataMappingService>(),
            Substitute.For<Vora.Application.Settings.ISystemSettingsRepository>(), Array.Empty<Vora.Plugins.Interfaces.IMetadataProvider>(),
            new Vora.Plugins.Interfaces.NullTaskProgressReporter(), Substitute.For<ILocalMetadataChangeFinder>(), NullLogger<MetadataManager>.Instance);

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMetadataManager)).Returns(_manager);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        _scopes.CreateScope().Returns(scope);

        _media.GetDisplayTitlesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new Dictionary<Guid, string>());
    }

    private TvShow Show(string title, params string[] episodeTitles)
    {
        var show = new TvShow { Title = title, LibraryId = _library.Id, Library = _library };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, TvShow = show, LibraryId = _library.Id, Library = _library };
        show.Seasons.Add(season);
        for (var number = 1; number <= episodeTitles.Length; number++)
        {
            season.Episodes.Add(new Episode { Title = episodeTitles[number - 1], EpisodeNumber = number, SeasonId = season.Id, Season = season, LibraryId = _library.Id, Library = _library });
        }

        _media.GetProjectedAsync(show.Id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns(nameof(TvShow));
        foreach (var item in new MediaItem[] { show, season }.Concat(season.Episodes))
        {
            _media.GetForMetadataSyncAsync(item.Id).Returns(item);
            _media.GetForRatingsRefreshAsync(item.Id).Returns(item);
        }
        _media.GetEpisodeIdsForShowAsync(show.Id).Returns(season.Episodes.Select(episode => episode.Id).ToList());
        return show;
    }

    private static Season SeasonOf(TvShow show) => show.Seasons.Single();

    private static List<Episode> EpisodesOf(TvShow show) => [.. SeasonOf(show).Episodes];

    private void ReplaceAllTakes(params MediaItem[] titles) =>
        _media.GetEnrichableMediaIdsAsync(_library.Id, Clicked).Returns(titles.Select(title => title.Id).ToList());

    private Task ReplaceAllAsync(CancellationToken cancellationToken) =>
        _manager.TriggerLibraryEnrichmentAsync(_library.Id, forceOverride: true, fullyRefreshedBefore: Clicked, cancellationToken: cancellationToken);

    [Fact]
    public async Task Replace_all_metadata_refreshes_every_season_and_episode_of_each_show_before_counting_the_show_as_done()
    {
        var wire = Show("The Wire", "The Target", "The Detail");
        var andor = Show("Andor", "Kassa");
        ReplaceAllTakes(wire, andor);

        await ReplaceAllAsync(TestContext.Current.CancellationToken);

        foreach (var episode in EpisodesOf(wire).Concat(EpisodesOf(andor)))
        {
            await _fetch.Received(1).GetTextMetadataAsync(episode);
            await _notifier.DidNotReceive().NotifyMediaItemUpdatedAsync(episode.Id);
        }
        await _fetch.Received(1).GetArtworkAsync(SeasonOf(wire));
        await _fetch.Received(1).GetRatingsAsync(SeasonOf(wire), Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _fetch.GetTextMetadataAsync(EpisodesOf(wire)[1]);
            _media.MarkFullyRefreshedAsync(wire.Id);
            _fetch.GetTextMetadataAsync(EpisodesOf(andor)[0]);
            _media.MarkFullyRefreshedAsync(andor.Id);
        });
    }

    [Fact]
    public async Task A_resumed_replace_all_leaves_a_show_finished_since_the_click_and_its_episodes_alone()
    {
        var finished = Show("Severance", "Good News About Hell");
        var pending = Show("Slow Horses", "Failure's Contagious");
        ReplaceAllTakes(pending);

        await ReplaceAllAsync(TestContext.Current.CancellationToken);

        await _media.Received(1).GetEnrichableMediaIdsAsync(_library.Id, Clicked);
        await _media.DidNotReceive().GetEpisodeIdsForShowAsync(finished.Id);
        await _fetch.DidNotReceive().GetTextMetadataAsync(EpisodesOf(finished)[0]);
        await _media.DidNotReceive().MarkFullyRefreshedAsync(finished.Id);
        await _fetch.Received(1).GetTextMetadataAsync(EpisodesOf(pending)[0]);
        await _media.Received(1).MarkFullyRefreshedAsync(pending.Id);
    }

    [Fact]
    public async Task An_episode_that_fails_does_not_stop_the_rest_of_the_show()
    {
        var show = Show("The Bear", "System", "Hands", "Brigade");
        var (failing, rest) = (EpisodesOf(show)[0], EpisodesOf(show)[1..]);
        _fetch.GetTextMetadataAsync(failing).Returns<(MetadataResult?, string, string)>(_ => throw new HttpRequestException("TVDB timed out"));
        ReplaceAllTakes(show);

        await ReplaceAllAsync(TestContext.Current.CancellationToken);

        foreach (var episode in rest) await _fetch.Received(1).GetTextMetadataAsync(episode);
        await _media.Received(1).MarkFullyRefreshedAsync(show.Id);
    }

    [Fact]
    public async Task A_show_stopped_part_way_through_its_episodes_is_not_counted_as_done()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var show = Show("Shogun", "Anjin", "Servants of Two Masters");
        var next = Show("Taskmaster", "Melon Buffet");
        _fetch.When(fetch => fetch.GetTextMetadataAsync(EpisodesOf(show)[0])).Do(_ => stop.Cancel());
        ReplaceAllTakes(show, next);

        var replacing = () => ReplaceAllAsync(stop.Token);

        await replacing.Should().ThrowAsync<OperationCanceledException>();
        await _fetch.DidNotReceive().GetTextMetadataAsync(EpisodesOf(show)[1]);
        await _media.DidNotReceive().MarkFullyRefreshedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Replace_all_metadata_still_replaces_a_film_and_looks_for_no_episodes()
    {
        var movie = new Movie { Title = "Heat", LibraryId = _library.Id, Library = _library };
        _media.GetProjectedAsync(movie.Id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns(nameof(Movie));
        _media.GetForMetadataSyncAsync(movie.Id).Returns(movie);
        _media.GetForRatingsRefreshAsync(movie.Id).Returns(movie);
        ReplaceAllTakes(movie);

        await ReplaceAllAsync(TestContext.Current.CancellationToken);

        await _fetch.Received(1).GetTextMetadataAsync(movie);
        await _media.DidNotReceive().GetEpisodeIdsForShowAsync(Arg.Any<Guid>());
        await _media.Received(1).MarkFullyRefreshedAsync(movie.Id);
    }
}
