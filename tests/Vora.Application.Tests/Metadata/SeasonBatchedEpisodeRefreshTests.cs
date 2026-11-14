using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Actors;
using Vora.Application.Analysis;
using Vora.Application.Artwork;
using Vora.Application.Collections;
using Vora.Application.Media;
using Vora.Application.Metadata;
using Vora.Application.Metadata.Local;
using Vora.Application.Settings;
using Vora.Domain.Entities.Actors;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.Local;
using ArtworkKind = Vora.Domain.Enums.ArtworkKind;

namespace Vora.Application.Tests.Metadata;

public class SeasonBatchedEpisodeRefreshTests
{
    private const string Tmdb = "tmdb_metadata";
    private const string Imdb = "Internet Movie Database";
    private const int EpisodesPerSeason = 5;
    private static readonly DateTime Clicked = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly MediaLibrary _library = new()
    {
        Name = "Shows",
        Type = LibraryType.TvShow,
        MetadataProviderId = Tmdb,
        ArtworkProviderId = "tmdb_artwork",
        ThirdPartyRating1ProviderId = "omdb_imdb",
        ThirdPartyRating2ProviderId = "tmdb_rating"
    };

    private readonly IMediaRepository _media = Substitute.For<IMediaRepository>();
    private readonly IActorRepository _actors = Substitute.For<IActorRepository>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly ILocalMediaReader _localMedia = Substitute.For<ILocalMediaReader>();
    private readonly IVideoFrameArtworkService _frames = Substitute.For<IVideoFrameArtworkService>();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();
    private readonly FakeTmdb _tmdb = new();
    private readonly CountingArtwork _artwork = new();
    private readonly CountingRatings _omdb = new("omdb_imdb", Imdb, score: 7.6m);
    private readonly CountingRatings _tmdbRatings = new("tmdb_rating", "TMDB", score: 9.9m, scoreComesWithMetadataFrom: Tmdb);
    private readonly TvShow _show;

    public SeasonBatchedEpisodeRefreshTests()
    {
        _actors.GetActorsByTmdbIdsOrNamesAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<IEnumerable<string>>()).Returns(_ => new List<Actor>());
        _localMedia.FindArtworkAsync(Arg.Any<Guid>()).Returns(_ => new List<MediaArtwork>());
        _frames.GetFrameArtworkAsync(Arg.Any<MediaItem>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ => new List<MediaArtwork>());
        _media.GetDisplayTitlesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns(new Dictionary<Guid, string>());
        _scopes.CreateScope().Returns(_ => Scope());

        _show = Show("Game of Thrones", seasons: 2);
    }

    private MetadataManager NewManager(params IMetadataProvider[] extraProviders)
    {
        var fetch = new MetadataFetchService([_tmdb, .. extraProviders], [_omdb, _tmdbRatings], [_artwork], _localMedia, _frames);
        var mapping = new MetadataMappingService(
            _media, Substitute.For<IMediaArtworkRepository>(), _actors, Substitute.For<ICollectionRepository>(),
            Substitute.For<IReferenceRepository>(), new ReferenceWriteGate(), Options.Create(new StoragePathsOptions()),
            Substitute.For<IPluginSettingsProvider>());
        return new MetadataManager(
            _media, _actors, _scopes, _notifier, fetch, mapping,
            Substitute.For<ISystemSettingsRepository>(), [],
            new NullTaskProgressReporter(), Substitute.For<ILocalMetadataChangeFinder>(), NullLogger<MetadataManager>.Instance);
    }

    private IServiceScope Scope()
    {
        var manager = NewManager(new LocalMetadataProvider(NullLogger<LocalMetadataProvider>.Instance));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMetadataManager)).Returns(manager);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private TvShow Show(string title, int seasons)
    {
        var show = new TvShow { Title = title, TmdbId = "1399", LibraryId = _library.Id, Library = _library };
        Known(show, nameof(TvShow));
        _tmdb.Show = new MetadataResult
        {
            TmdbId = "1399",
            ImdbId = "tt0944947",
            Title = title,
            PosterUrl = "https://image.tmdb.org/t/p/w500/show.jpg",
            Seasons = Enumerable.Range(1, seasons)
                .Select(number => new SeasonResult { Id = 3600 + number, SeasonNumber = number, Name = $"Season {number}", PosterUrl = $"https://image.tmdb.org/t/p/w500/s{number}.jpg" })
                .ToList()
        };

        var episodeIds = new List<Guid>();
        for (var seasonNumber = 1; seasonNumber <= seasons; seasonNumber++)
        {
            var season = new Season { Title = $"Season {seasonNumber}", SeasonNumber = seasonNumber, TvShowId = show.Id, TvShow = show, LibraryId = _library.Id, Library = _library };
            show.Seasons.Add(season);
            Known(season, nameof(Season));

            var answer = new Dictionary<int, MetadataResult>();
            for (var number = 1; number <= EpisodesPerSeason; number++)
            {
                var episode = new Episode { Title = $"S{seasonNumber:00}E{number:00}.mkv", EpisodeNumber = number, SeasonId = season.Id, Season = season, LibraryId = _library.Id, Library = _library };
                season.Episodes.Add(episode);
                episodeIds.Add(episode.Id);
                Known(episode, nameof(Episode));
                answer[number] = EpisodeAnswer(seasonNumber, number);
            }
            _tmdb.Seasons[seasonNumber] = answer;
        }

        _media.GetEpisodeIdsForShowAsync(show.Id).Returns(episodeIds);
        return show;
    }

    private static string Still(int season, int episode) => $"https://image.tmdb.org/t/p/w1280/still_{season}_{episode}.jpg";

    private static MetadataResult EpisodeAnswer(int season, int episode) => new()
    {
        TmdbId = $"{63000 + season * 100 + episode}",
        Title = $"Chapter {season}.{episode}",
        Overview = "Winter is coming.",
        Rating = 8.1m,
        PosterUrl = $"https://image.tmdb.org/t/p/w500/still_{season}_{episode}.jpg",
        BackgroundUrl = Still(season, episode),
        Cast =
        [
            new CastMemberResult { TmdbId = 9000 + season * 100 + episode, Name = $"Guest {season}.{episode}", CharacterName = "Stranger" },
            new CastMemberResult { TmdbId = 47005, Name = "Alan Taylor", Roles = CastRole.Director }
        ]
    };

    private void Known(MediaItem item, string type)
    {
        _media.GetProjectedAsync(item.Id, Arg.Any<Expression<Func<MediaItem, string>>>()).Returns(type);
        _media.GetForMetadataSyncAsync(item.Id).Returns(item);
        _media.GetForRatingsRefreshAsync(item.Id).Returns(item);
        if (item is Season) _media.GetForBasicUpdateAsync(item.Id).Returns(item);
    }

    private List<Episode> Episodes => [.. _show.Seasons.SelectMany(season => season.Episodes)];

    private Task RefreshAsync(string how) => how switch
    {
        "title menu" => NewManager().RefreshTitleAsync(_show.Id, forceOverride: true, TestContext.Current.CancellationToken),
        _ => ReplaceAllAsync()
    };

    private Task ReplaceAllAsync()
    {
        _media.GetEnrichableMediaIdsAsync(_library.Id, Clicked).Returns([_show.Id]);
        return NewManager().TriggerLibraryEnrichmentAsync(_library.Id, forceOverride: true, fullyRefreshedBefore: Clicked, cancellationToken: TestContext.Current.CancellationToken);
    }

    private void EpisodesToRate(bool missingOnly, DateTime? checkedBefore, params Episode[] episodes) =>
        _media.GetEpisodeIdsToRateAsync(_library.Id, missingOnly, checkedBefore)
            .Returns(episodes.Select(episode => (_show.Id, episode.Id)).ToList());

    [Theory]
    [InlineData("title menu")]
    [InlineData("replace all")]
    public async Task A_show_costs_one_request_for_itself_and_one_per_season_and_nothing_per_episode(string how)
    {
        await RefreshAsync(how);

        _tmdb.ShowRequests.Should().Be(1);
        _tmdb.SeasonRequests.Should().Equal(1, 2);
        _tmdb.EpisodeRequests.Should().Be(0);
        _artwork.AskedAbout.Should().NotContain(nameof(Episode)).And.Equal(nameof(Season), nameof(Season), nameof(TvShow));
        _omdb.AskedAbout.Should().NotContain(nameof(Episode)).And.Equal(nameof(Season), nameof(Season), nameof(TvShow));
        _tmdbRatings.AskedAbout.Should().NotContain(nameof(Episode)).And.Equal(nameof(Season), nameof(Season), nameof(TvShow));
        foreach (var episode in Episodes) await _notifier.DidNotReceive().NotifyMediaItemUpdatedAsync(episode.Id);
    }

    [Fact]
    public async Task Replace_all_counts_the_show_as_done_only_after_its_episodes()
    {
        await ReplaceAllAsync();

        await _media.Received(1).MarkFullyRefreshedAsync(_show.Id);
        Episodes.Should().OnlyContain(episode => episode.LastMetadataRefresh != null);
    }

    [Fact]
    public async Task Every_episode_takes_its_text_credits_and_still_from_its_seasons_answer()
    {
        await RefreshAsync("title menu");

        foreach (var episode in Episodes)
        {
            episode.Title.Should().Be($"Chapter {episode.Season.SeasonNumber}.{episode.EpisodeNumber}");
            episode.Overview.Should().Be("Winter is coming.");
            episode.PosterUrl.Should().Be(Still(episode.Season.SeasonNumber, episode.EpisodeNumber));
            await _media.Received().AddMediaCastMembersAsync(Arg.Is<IEnumerable<MediaCastMember>>(links =>
                links.Any(link => link.MediaItemId == episode.Id && link.Roles == MediaCastRole.Director)
                && links.Any(link => link.MediaItemId == episode.Id && link.CharacterName == "Stranger")));
        }
    }

    [Fact]
    public async Task The_episode_score_in_the_season_answer_fills_only_the_slot_set_to_TMDB_ratings()
    {
        await RefreshAsync("title menu");

        Episodes.Should().OnlyContain(episode =>
            episode.ThirdPartyRating1 == null && episode.ThirdPartyRating1Name == null
            && episode.ThirdPartyRating2 == 8.1m && episode.ThirdPartyRating2Name == "TMDB"
            && episode.RatingsCheckedAt == null);
    }

    [Fact]
    public async Task A_library_that_rates_with_OMDb_alone_takes_no_score_from_the_season_answer()
    {
        _library.ThirdPartyRating2ProviderId = null;

        await RefreshAsync("title menu");

        Episodes.Should().OnlyContain(episode => episode.ThirdPartyRating1 == null && episode.ThirdPartyRating2 == null);
    }

    [Fact]
    public async Task An_episode_without_a_still_keeps_the_picture_beside_its_file()
    {
        _library.PreferLocalArtwork = true;
        var plain = Episodes[3];
        _tmdb.Seasons[1][plain.EpisodeNumber].PosterUrl = null;
        _tmdb.Seasons[1][plain.EpisodeNumber].BackgroundUrl = null;
        var thumb = new MediaArtwork { MediaItemId = plain.Id, Url = $"/api/artwork/local/{plain.Id}/episode-thumb.jpg", Kind = ArtworkKind.Backdrop, ProviderId = LocalArtworkProvider.ProviderId };
        _localMedia.FindArtworkAsync(plain.Id).Returns(_ => new List<MediaArtwork> { thumb });

        await RefreshAsync("title menu");

        plain.PosterUrl.Should().Be(thumb.Url);
        Episodes.Except([plain]).Should().OnlyContain(episode => episode.PosterUrl == Still(episode.Season.SeasonNumber, episode.EpisodeNumber));
        _artwork.AskedAbout.Should().NotContain(nameof(Episode));
    }

    [Fact]
    public async Task An_episode_of_a_show_kept_local_still_gets_a_frame_of_its_video()
    {
        _show.LockedFields.Add(MediaMatchManager.UnmatchedLock);
        var frame = new MediaArtwork { Url = "/api/artwork/custom/frame_backdrop.jpg", Kind = ArtworkKind.Backdrop, ProviderId = VideoFrameArtworkService.ProviderId };
        _frames.GetFrameArtworkAsync(Arg.Is<MediaItem>(item => item is Episode), true, true, Arg.Any<CancellationToken>()).Returns(_ => new List<MediaArtwork> { frame });

        await NewManager(new LocalMetadataProvider(NullLogger<LocalMetadataProvider>.Instance)).RefreshTitleAsync(_show.Id, forceOverride: true, TestContext.Current.CancellationToken);

        Episodes.Should().OnlyContain(episode => episode.PosterUrl == frame.Url);
        _tmdb.SeasonRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_first_scan_names_and_pictures_episodes_at_once_and_rates_them_once_in_its_ratings_pass()
    {
        foreach (var episode in Episodes) episode.ImdbId = $"tt{episode.Id.GetHashCode():x}";
        EpisodesToRate(missingOnly: true, checkedBefore: null, [.. Episodes]);

        await NewManager().RefreshTitleAsync(_show.Id, forceOverride: false, TestContext.Current.CancellationToken);

        Episodes.Should().OnlyContain(episode => episode.Title.StartsWith("Chapter ") && episode.PosterUrl != null);
        _omdb.AskedAbout.Should().NotContain(nameof(Episode));

        await NewManager().TriggerLibraryEnrichmentAsync(_library.Id, forceOverride: false, cancellationToken: TestContext.Current.CancellationToken);

        _omdb.Asked.Where(asked => asked.MediaType == nameof(Episode)).Select(asked => asked.ImdbId)
            .Should().BeEquivalentTo(Episodes.Select(episode => episode.ImdbId));
        _tmdbRatings.AskedAbout.Should().NotContain(nameof(Episode));
        Episodes.Should().OnlyContain(episode => episode.ThirdPartyRating1 == 7.6m && episode.ThirdPartyRating2 == 8.1m && episode.RatingsCheckedAt != null);
    }

    [Fact]
    public async Task The_library_ratings_refresh_rates_episodes_after_the_titles()
    {
        var (first, second) = (Episodes[0], Episodes[6]);
        first.ImdbId = "tt1480055";
        second.ImdbId = "tt1971833";
        first.ThirdPartyRating2 = 8.1m;
        first.ThirdPartyRating2Name = "TMDB";
        _media.GetEnrichableMediaIdsAsync(_library.Id, ratingsCheckedBefore: Clicked).Returns([_show.Id]);
        EpisodesToRate(missingOnly: false, checkedBefore: Clicked, first, second);

        await NewManager().TriggerLibraryRatingsRefreshAsync(_library.Id, forceOverride: true, checkedBefore: Clicked, cancellationToken: TestContext.Current.CancellationToken);

        _omdb.Asked.Select(asked => asked.ImdbId).Should().Equal(null, "tt1480055", "tt1971833");
        _tmdbRatings.AskedAbout.Should().Equal(nameof(TvShow));
        first.ThirdPartyRating1.Should().Be(7.6m);
        first.ThirdPartyRating2.Should().Be(8.1m, "TMDB's episode score comes with the season answer, and the ratings pass keeps it");
        second.ThirdPartyRating2.Should().BeNull();
        new[] { first, second }.Should().OnlyContain(episode => episode.RatingsCheckedAt != null);
        await _notifier.DidNotReceive().NotifyMediaItemUpdatedAsync(first.Id);
        await _notifier.DidNotReceive().NotifyMediaItemUpdatedAsync(second.Id);
        await _notifier.Received(2).NotifyMediaItemUpdatedAsync(_show.Id);
    }

    [Fact]
    public async Task The_ratings_refresh_stops_where_OMDb_runs_out_and_leaves_the_rest_unchecked_for_the_next_pass()
    {
        _library.ThirdPartyRating2ProviderId = null;
        _omdb.RunsOutAfter = 2;
        var episodes = Episodes.Take(3).ToArray();
        _media.GetEnrichableMediaIdsAsync(_library.Id, ratingsCheckedBefore: Clicked).Returns([_show.Id]);
        EpisodesToRate(missingOnly: false, checkedBefore: Clicked, episodes);

        await NewManager().TriggerLibraryRatingsRefreshAsync(_library.Id, forceOverride: true, checkedBefore: Clicked, cancellationToken: TestContext.Current.CancellationToken);

        _omdb.AskedAbout.Should().Equal(nameof(TvShow), nameof(Episode));
        episodes[0].RatingsCheckedAt.Should().NotBeNull();
        episodes[1..].Should().OnlyContain(episode => episode.RatingsCheckedAt == null);
        await _media.DidNotReceive().GetForRatingsRefreshAsync(episodes[1].Id);
    }

    [Fact]
    public async Task With_OMDb_already_out_of_requests_the_pass_stops_before_it_reaches_the_episodes()
    {
        _library.ThirdPartyRating2ProviderId = null;
        _omdb.RunsOutAfter = 0;
        _media.GetEnrichableMediaIdsAsync(_library.Id, ratingsCheckedBefore: Clicked).Returns([_show.Id]);

        await NewManager().TriggerLibraryRatingsRefreshAsync(_library.Id, forceOverride: true, checkedBefore: Clicked, cancellationToken: TestContext.Current.CancellationToken);

        _omdb.Asked.Should().BeEmpty();
        _show.RatingsCheckedAt.Should().BeNull();
        await _media.DidNotReceive().GetEpisodeIdsToRateAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task The_plain_ratings_refresh_takes_only_episodes_missing_a_rating()
    {
        await NewManager().TriggerLibraryRatingsRefreshAsync(_library.Id, cancellationToken: TestContext.Current.CancellationToken);

        await _media.Received(1).GetEpisodeIdsToRateAsync(_library.Id, true, null);
    }

    [Fact]
    public async Task Refreshing_one_episode_from_its_menu_rates_that_episode_and_keeps_the_TMDB_score_from_its_season()
    {
        var episode = Episodes[2];
        episode.ImdbId = "tt1851398";

        await NewManager().RefreshTitleAsync(episode.Id, forceOverride: true, TestContext.Current.CancellationToken);

        _tmdb.SeasonRequests.Should().Equal(1);
        _artwork.AskedAbout.Should().BeEmpty();
        _omdb.Asked.Should().ContainSingle().Which.ImdbId.Should().Be("tt1851398");
        _tmdbRatings.AskedAbout.Should().BeEmpty();
        episode.ThirdPartyRating1.Should().Be(7.6m);
        episode.ThirdPartyRating2.Should().Be(8.1m);
    }

    private sealed class FakeTmdb : IMetadataProvider
    {
        public MetadataResult? Show { get; set; }
        public Dictionary<int, Dictionary<int, MetadataResult>> Seasons { get; } = new();
        public int ShowRequests { get; private set; }
        public List<int> SeasonRequests { get; } = [];
        public int EpisodeRequests { get; private set; }

        public string Id => Tmdb;
        public string Name => "TMDB";
        public string Version => "1.0.0";
        public string Description => "Counts requests";
        public bool IsSystemPlugin => true;
        public string Type => "Metadata";
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

        public Task<MetadataResult?> FetchTvShowMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default)
        {
            ShowRequests++;
            return Task.FromResult(Show);
        }

        public Task<MetadataResult?> FetchTvShowMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default)
        {
            ShowRequests++;
            return Task.FromResult(Show);
        }

        public Task<IReadOnlyDictionary<int, MetadataResult>?> FetchSeasonEpisodesAsync(string showId, int seasonNumber, CancellationToken cancellationToken = default)
        {
            SeasonRequests.Add(seasonNumber);
            return Task.FromResult<IReadOnlyDictionary<int, MetadataResult>?>(Seasons.GetValueOrDefault(seasonNumber));
        }

        public Task<MetadataResult?> FetchEpisodeMetadataAsync(string showTmdbId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
        {
            EpisodeRequests++;
            return Task.FromResult<MetadataResult?>(null);
        }

        public Task<MetadataResult?> FetchMovieMetadataAsync(string query, int? year = null, CancellationToken cancellationToken = default) => Task.FromResult<MetadataResult?>(null);
        public Task<MetadataResult?> FetchMovieMetadataByIdAsync(string id, string source, CancellationToken cancellationToken = default) => Task.FromResult<MetadataResult?>(null);
        public Task<ActorMetadataResult?> FetchActorMetadataAsync(int personId, CancellationToken cancellationToken = default) => Task.FromResult<ActorMetadataResult?>(null);
    }

    private sealed class CountingArtwork : IArtworkProvider
    {
        public List<string> AskedAbout { get; } = [];

        public string Id => "tmdb_artwork";
        public string Name => "TMDB Artwork";
        public string Version => "1.0.0";
        public string Description => "Counts requests";
        public bool IsSystemPlugin => true;
        public string Type => "Artwork";
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

        public Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null, CancellationToken cancellationToken = default)
        {
            AskedAbout.Add(mediaType);
            return Task.FromResult<IEnumerable<ArtworkResult>>([]);
        }
    }

    private sealed class CountingRatings(string id, string source, decimal score, string? scoreComesWithMetadataFrom = null) : IRatingsProvider
    {
        public List<(string MediaType, string? ImdbId)> Asked { get; } = [];
        public List<string> AskedAbout => [.. Asked.Select(asked => asked.MediaType)];
        public int? RunsOutAfter { get; set; }

        public string Id => id;
        public string Name => source;
        public string Version => "1.0.0";
        public string Description => "Counts requests";
        public bool IsSystemPlugin => true;
        public string Type => "Ratings";
        public string RatingSourceName => source;
        public string? ScoreComesWithMetadataFrom => scoreComesWithMetadataFrom;
        public bool IsCurrentlyAvailable => RunsOutAfter == null || Asked.Count < RunsOutAfter;
        public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => [];

        public Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType, CancellationToken cancellationToken = default)
        {
            Asked.Add((mediaType, imdbId));
            return Task.FromResult<decimal?>(score);
        }
    }
}
