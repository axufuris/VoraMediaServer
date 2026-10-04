using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Application.Podcasts;
using Vora.Application.Podcasts.ViewModels;
using Vora.Application.SmartLists;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.ViewModels;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.SmartLists;

public class SmartListSourceResolverTests
{
    private readonly IChannelFavoriteRepository _favorites = Substitute.For<IChannelFavoriteRepository>();
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly IIptvEpgService _epg = Substitute.For<IIptvEpgService>();
    private readonly IPodcastManager _podcasts = Substitute.For<IPodcastManager>();
    private readonly IMusicManager _music = Substitute.For<IMusicManager>();
    private readonly IIptvRepository _iptv = Substitute.For<IIptvRepository>();
    private readonly SmartListSourceResolver _resolver;

    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly IptvPlaylist _allowed = new() { Id = Guid.NewGuid(), Name = "Allowed" };
    private readonly IptvPlaylist _blocked = new() { Id = Guid.NewGuid(), Name = "Blocked" };
    private readonly UserVM _user;

    public SmartListSourceResolverTests()
    {
        _user = new UserVM
        {
            Id = _accountId,
            HasAllIptvAccess = false,
            AllowedIptvPlaylistIds = new List<Guid> { _allowed.Id },
            Profiles = new List<UserProfileVM> { new() { Id = _profileId, HasAllIptvAccess = true } }
        };
        _users.GetUserAccountAsync(_accountId).Returns(_user);
        _epg.GetFilteredGuideAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>>());
        _resolver = new SmartListSourceResolver(_favorites, _users, _epg, _podcasts, _music, _iptv, NullLogger<SmartListSourceResolver>.Instance);
    }

    private SmartListViewer Viewer => new(_accountId, _profileId, false, true, new List<Guid>(), new List<string>(), new List<string>(), false, new MusicAccessFilter());

    private static SmartList List(SmartListSource source, SmartListSortBy sortBy = SmartListSortBy.TitleAsc, int maxItems = 20) =>
        new() { Id = Guid.NewGuid(), Title = "Row", Source = source, SortBy = sortBy, MaxItems = maxItems };

    private static FavoriteChannel Favorite(IptvPlaylist playlist, string name, IptvChannelKind kind = IptvChannelKind.Tv, int minutesAgo = 0, string? externalId = null, Action<IptvChannel>? configure = null)
    {
        var channel = new IptvChannel
        {
            Id = Guid.NewGuid(),
            ExternalChannelId = externalId ?? name.ToLowerInvariant(),
            Name = name,
            StreamUrl = "https://example.test/" + name,
            Kind = kind,
            PlaylistId = playlist.Id,
            Playlist = playlist
        };
        configure?.Invoke(channel);
        return new FavoriteChannel(channel, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    [Fact]
    public async Task Only_favorites_the_profile_can_watch_right_now_are_shown()
    {
        var inactive = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Off", IsActive = false };
        _user.AllowedIptvPlaylistIds.Add(inactive.Id);
        _favorites.GetFavoriteChannelsAsync(_profileId, IptvChannelKind.Tv).Returns(new List<FavoriteChannel>
        {
            Favorite(_allowed, "News"),
            Favorite(_blocked, "Blocked Sports"),
            Favorite(inactive, "Inactive"),
            Favorite(_allowed, "Hidden", configure: c => c.IsHiddenByAdmin = true),
            Favorite(_allowed, "Broken", configure: c => c.IsHealthy = false)
        });

        var entries = await _resolver.ResolveAsync(List(SmartListSource.FavoriteChannels), null, Viewer);

        entries.Select(e => e.Title).Should().Equal("News");
        entries[0].Kind.Should().Be(SmartListEntryKind.Channel);
        (entries[0].Channel?.StreamUrl).Should().Be("https://example.test/News");
    }

    [Fact]
    public async Task The_same_channel_in_two_playlists_shows_once()
    {
        _user.HasAllIptvAccess = true;
        _favorites.GetFavoriteChannelsAsync(_profileId, IptvChannelKind.Tv).Returns(new List<FavoriteChannel>
        {
            Favorite(_allowed, "CNN", externalId: "cnn.us"),
            Favorite(_blocked, "CNN HD", externalId: "CNN.US")
        });

        var entries = await _resolver.ResolveAsync(List(SmartListSource.FavoriteChannels), null, Viewer);

        entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Favorites_follow_the_row_sort_and_size()
    {
        _favorites.GetFavoriteChannelsAsync(_profileId, IptvChannelKind.Radio).Returns(new List<FavoriteChannel>
        {
            Favorite(_allowed, "Jazz", IptvChannelKind.Radio, minutesAgo: 30),
            Favorite(_allowed, "Blues", IptvChannelKind.Radio, minutesAgo: 10),
            Favorite(_allowed, "Classical", IptvChannelKind.Radio, minutesAgo: 20)
        });

        var byName = await _resolver.ResolveAsync(List(SmartListSource.FavoriteStations, SmartListSortBy.TitleAsc, 2), null, Viewer);
        var byRecent = await _resolver.ResolveAsync(List(SmartListSource.FavoriteStations, SmartListSortBy.DateAddedDesc), null, Viewer);

        byName.Select(e => e.Title).Should().Equal("Blues", "Classical");
        byName.Should().OnlyContain(e => e.Kind == SmartListEntryKind.Station);
        byRecent.Select(e => e.Title).Should().Equal("Blues", "Classical", "Jazz");
    }

    [Fact]
    public async Task A_tv_favorite_says_what_is_on_now()
    {
        var news = Favorite(_allowed, "News", configure: c => c.GroupTitle = "Local");
        var sports = Favorite(_allowed, "Sports", configure: c => c.GroupTitle = "Sport");
        _favorites.GetFavoriteChannelsAsync(_profileId, IptvChannelKind.Tv).Returns(new List<FavoriteChannel> { news, sports });
        var now = DateTime.UtcNow;
        _epg.GetFilteredGuideAsync(_accountId, _profileId, Arg.Any<List<string>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new Dictionary<string, List<IptvProgramDto>>
            {
                ["news"] = new()
                {
                    new IptvProgramDto { Id = "1", ChannelId = "news", Title = "Earlier", StartTime = now.AddHours(-2), EndTime = now.AddHours(-1) },
                    new IptvProgramDto { Id = "2", ChannelId = "news", Title = "Evening News", StartTime = now.AddMinutes(-10), EndTime = now.AddMinutes(20) }
                }
            });

        var entries = await _resolver.ResolveAsync(List(SmartListSource.FavoriteChannels), null, Viewer);

        entries.Single(e => e.Title == "News").Subtitle.Should().Be("Evening News");
        entries.Single(e => e.Title == "Sports").Subtitle.Should().Be("Sport");
    }

    [Fact]
    public async Task Without_a_profile_there_are_no_personal_rows()
    {
        var viewer = Viewer with { ProfileId = null };

        (await _resolver.ResolveAsync(List(SmartListSource.FavoriteChannels), null, viewer)).Should().BeEmpty();
        (await _resolver.ResolveAsync(List(SmartListSource.NewPodcastEpisodes), null, viewer)).Should().BeEmpty();
        (await _resolver.ResolveAsync(List(SmartListSource.RecentRecordings), null, viewer)).Should().BeEmpty();
    }

    [Fact]
    public async Task Unplayed_podcast_rows_skip_finished_episodes_and_pass_the_window_through()
    {
        _podcasts.GetRecentEpisodesAsync(_profileId, Arg.Any<int>(), Arg.Any<int?>()).Returns(new List<PodcastFeedEpisodeVM>
        {
            new() { Id = Guid.NewGuid(), Title = "Done", ShowTitle = "Show", IsPlayed = true },
            new() { Id = Guid.NewGuid(), Title = "New", ShowTitle = "Show", ShowArtworkUrl = "https://feeds.test/art.jpg" }
        });

        var entries = await _resolver.ResolveAsync(List(SmartListSource.NewPodcastEpisodes, maxItems: 5), new SmartListRulesDto { UnwatchedOnly = true, Days = 14 }, Viewer);

        entries.Select(e => e.Title).Should().Equal("New");
        entries[0].Subtitle.Should().Be("Show");
        entries[0].ImageUrl.Should().Be("https://feeds.test/art.jpg");
        await _podcasts.Received(1).GetRecentEpisodesAsync(_profileId, 20, 14);
    }

    [Fact]
    public async Task Album_rows_map_the_sort_and_the_library()
    {
        var libraryId = Guid.NewGuid();
        _music.GetAlbumsAsync(Arg.Any<Guid?>(), Arg.Any<MusicAccessFilter>(), Arg.Any<AlbumSortOrder>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new AlbumPageVM { Items = new List<AlbumVM> { new() { Id = Guid.NewGuid(), Title = "Elephunk", ArtistName = "The Black Eyed Peas" } } });
        var list = List(SmartListSource.RecentlyAddedMusic, SmartListSortBy.TopRated, 12);
        list.LibraryId = libraryId;

        var entries = await _resolver.ResolveAsync(list, null, Viewer);

        entries.Single().Subtitle.Should().Be("The Black Eyed Peas");
        await _music.Received(1).GetAlbumsAsync(libraryId, Arg.Any<MusicAccessFilter>(), AlbumSortOrder.Popular, 0, 12, Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Recording_rows_hold_finished_recordings_newest_first()
    {
        var channel = new IptvChannel { ExternalChannelId = "news", Name = "News 4", StreamUrl = "https://example.test/news", LogoUrl = "https://logos.test/news.png" };
        var schedule = new IptvRecordingSchedule { Title = "Show", Channel = channel };
        IptvRecordingSession Session(string title, IptvRecordingSessionStatus status, int daysAgo) => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = status,
            StartTime = DateTime.UtcNow.AddDays(-daysAgo),
            EndTime = DateTime.UtcNow.AddDays(-daysAgo).AddHours(1),
            OutputFilePath = status == IptvRecordingSessionStatus.Completed ? "/recordings/" + title + ".mp4" : null,
            Schedule = schedule
        };
        _iptv.GetSessionsForProfileAsync(_profileId).Returns(new List<IptvRecordingSession>
        {
            Session("Old", IptvRecordingSessionStatus.Completed, 20),
            Session("Newer", IptvRecordingSessionStatus.Completed, 1),
            Session("Still going", IptvRecordingSessionStatus.Recording, 0),
            Session("Failed", IptvRecordingSessionStatus.Failed, 2),
            Session("Newest", IptvRecordingSessionStatus.Completed, 0)
        });

        var entries = await _resolver.ResolveAsync(List(SmartListSource.RecentRecordings), new SmartListRulesDto { Days = 7 }, Viewer);

        entries.Select(e => e.Title).Should().Equal("Newest", "Newer");
        entries[0].Subtitle.Should().Be("News 4");
        entries[0].ImageUrl.Should().Be("https://logos.test/news.png");
        (entries[0].Recording?.Status).Should().Be("Completed");
    }
}
