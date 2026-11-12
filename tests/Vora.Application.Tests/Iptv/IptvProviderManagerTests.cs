using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvProviderManagerTests
{
    private readonly IIptvRepository _repo = Substitute.For<IIptvRepository>();
    private readonly IUserManager _users = Substitute.For<IUserManager>();
    private readonly ITaskQueueManager _tasks = Substitute.For<ITaskQueueManager>();
    private readonly ITimeshiftCoordinator _timeshift = Substitute.For<ITimeshiftCoordinator>();
    private readonly PlainSecretProtector _protector = new();
    private readonly StubUpstream _upstream = new();
    private readonly TunerRegistry _tuners = new();
    private readonly IptvManager _manager;

    public IptvProviderManagerTests()
    {
        var resolver = new IptvUpstreamResolver(_protector);
        _manager = new IptvManager(_repo, Substitute.For<IIptvEpgService>(), _users, _tasks, _timeshift, _tuners,
            Substitute.For<IStreamingTokenSigner>(), Substitute.For<ITimeshiftVideo>(), new IptvChannelLoader(_upstream, resolver), resolver, _protector,
            NullLogger<IptvManager>.Instance);
    }

    private static IptvPlaylistSettingsDto XtreamSettings(string? password = XtreamFixtures.Password) => new()
    {
        Name = "Paid",
        SourceType = IptvSourceType.Xtream,
        ServerUrl = "provider.example.com:8080",
        Username = XtreamFixtures.Username,
        NewPassword = password,
        DefaultChannelKind = IptvChannelKind.Tv
    };

    private void ServeXtream(string account = XtreamFixtures.ActiveAccount) =>
        _upstream.OnAction(null, account)
            .OnAction("get_live_categories", XtreamFixtures.Categories)
            .OnAction("get_live_streams", XtreamFixtures.LiveStreams);

    [Fact]
    public async Task Adding_an_xtream_account_stores_the_password_sealed_and_records_the_account()
    {
        ServeXtream();
        IptvPlaylist? saved = null;
        await _repo.AddPlaylistAsync(Arg.Do<IptvPlaylist>(p => saved = p));

        var view = await _manager.AddPlaylistAsync(XtreamSettings());

        saved.Should().NotBeNull();
        saved!.ServerUrl.Should().Be("http://provider.example.com:8080");
        saved.PasswordCiphertext.Should().NotBeNull().And.NotContain(XtreamFixtures.Password);
        _protector.Unprotect(saved.PasswordCiphertext!).Should().Be(XtreamFixtures.Password);
        saved.AccountStatus.Should().Be("Active");
        saved.AccountMaxConnections.Should().Be(2);
        saved.LastError.Should().BeNull();
        await _repo.Received(1).UpdateChannelsAsync(saved.Id, Arg.Is<List<IptvChannel>>(c => c.Count == 4 && c.All(x => !x.StreamUrl.Contains(XtreamFixtures.Password))));
        view.PasswordIsSet.Should().BeTrue();
        view.ConnectionLimit.Should().Be(2);
    }

    [Fact]
    public async Task A_rejected_account_is_saved_with_a_readable_error_and_no_address()
    {
        _upstream.OnAction(null, XtreamFixtures.RejectedAccount);
        IptvPlaylist? saved = null;
        await _repo.AddPlaylistAsync(Arg.Do<IptvPlaylist>(p => saved = p));

        var view = await _manager.AddPlaylistAsync(XtreamSettings());

        view.LastError.Should().Be("The provider rejected the username or password.");
        saved!.AccountStatus.Should().BeNull();
        await _repo.DidNotReceiveWithAnyArgs().UpdateChannelsAsync(default, default!);
    }

    [Fact]
    public async Task An_expired_account_keeps_its_status_for_the_admin()
    {
        ServeXtream(XtreamFixtures.ExpiredStatusAccount);

        var view = await _manager.AddPlaylistAsync(XtreamSettings());

        view.LastError.Should().Be("The provider account has expired.");
        view.AccountStatus.Should().Be("Expired");
    }

    [Fact]
    public async Task An_xtream_account_needs_its_password()
    {
        var act = async () => await _manager.AddPlaylistAsync(XtreamSettings(password: null));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*password*");
        await _repo.DidNotReceiveWithAnyArgs().AddPlaylistAsync(default!);
    }

    [Fact]
    public async Task An_m3u_failure_never_puts_the_address_in_the_error()
    {
        _upstream.Fail(_ => true, new HttpRequestException("Connection refused (http://provider.example.com/get.php?username=viewer&password=example-password)"));
        IptvPlaylist? saved = null;
        await _repo.AddPlaylistAsync(Arg.Do<IptvPlaylist>(p => saved = p));

        var view = await _manager.AddPlaylistAsync(new IptvPlaylistSettingsDto
        {
            Name = "Paid M3U",
            M3uUrl = "http://provider.example.com/get.php?username=viewer&password=example-password",
            DefaultChannelKind = IptvChannelKind.Tv
        });

        view.LastError.Should().Be("The provider could not be reached: Connection refused (http://provider.example.com/get.php?username=***&password=***");
        saved!.LastError.Should().NotContain("example-password");
    }

    [Fact]
    public async Task Changing_only_the_password_reloads_the_channels_and_an_update_without_one_keeps_it()
    {
        ServeXtream();
        var playlist = new IptvPlaylist
        {
            Name = "Paid",
            SourceType = IptvSourceType.Xtream,
            ServerUrl = XtreamFixtures.Server,
            Username = XtreamFixtures.Username,
            PasswordCiphertext = _protector.Protect("old password"),
            TunerProfile = new IptvTunerProfile()
        };
        _repo.GetPlaylistByIdAsync(playlist.Id).Returns(playlist);

        await _manager.UpdatePlaylistAsync(playlist.Id, XtreamSettings());
        _protector.Unprotect(playlist.PasswordCiphertext!).Should().Be(XtreamFixtures.Password);
        await _repo.Received(1).UpdateChannelsAsync(playlist.Id, Arg.Any<List<IptvChannel>>());
        _tasks.Received(1).QueueIptvEpgSync();

        _repo.ClearReceivedCalls();
        _tasks.ClearReceivedCalls();
        await _manager.UpdatePlaylistAsync(playlist.Id, new IptvPlaylistSettingsDto { Name = "Renamed", DefaultChannelKind = IptvChannelKind.Tv });

        playlist.Name.Should().Be("Renamed");
        _protector.Unprotect(playlist.PasswordCiphertext!).Should().Be(XtreamFixtures.Password);
        playlist.Username.Should().Be(XtreamFixtures.Username);
        await _repo.DidNotReceiveWithAnyArgs().UpdateChannelsAsync(default, default!);
        _tasks.DidNotReceive().QueueIptvEpgSync();
    }

    [Fact]
    public async Task Clearing_the_password_of_an_m3u_login_removes_it()
    {
        var playlist = new IptvPlaylist { Name = "Login", M3uUrl = "https://iptv.example.com/list.m3u", Username = "viewer", PasswordCiphertext = _protector.Protect("example-password"), TunerProfile = new IptvTunerProfile() };
        _repo.GetPlaylistByIdAsync(playlist.Id).Returns(playlist);
        _upstream.On(_ => true, "#EXTM3U\n", "audio/x-mpegurl");

        await _manager.UpdatePlaylistAsync(playlist.Id, new IptvPlaylistSettingsDto { Name = "Login", Username = "", ClearPassword = true, DefaultChannelKind = IptvChannelKind.Tv });

        playlist.PasswordCiphertext.Should().BeNull();
        playlist.Username.Should().BeNull();
    }

    [Fact]
    public async Task Testing_a_connection_reports_the_account_and_channel_count_without_saving()
    {
        ServeXtream();

        var result = await _manager.TestConnectionAsync(XtreamSettings(), existingId: null, TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.AccountStatus.Should().Be("Active");
        result.AccountMaxConnections.Should().Be(2);
        result.AccountExpiresAt.Should().Be(new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.ChannelCount.Should().Be(4);
        await _repo.DidNotReceiveWithAnyArgs().AddPlaylistAsync(default!);
        await _repo.DidNotReceiveWithAnyArgs().UpdatePlaylistAsync(default!);
        await _repo.DidNotReceiveWithAnyArgs().UpdateChannelsAsync(default, default!);
    }

    [Fact]
    public async Task Testing_a_saved_account_uses_its_stored_password()
    {
        ServeXtream();
        var playlist = new IptvPlaylist { Name = "Paid", SourceType = IptvSourceType.Xtream, ServerUrl = XtreamFixtures.Server, Username = XtreamFixtures.Username, PasswordCiphertext = _protector.Protect(XtreamFixtures.Password) };
        _repo.GetPlaylistByIdAsync(playlist.Id).Returns(playlist);

        var result = await _manager.TestConnectionAsync(XtreamSettings(password: null), playlist.Id, TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        _upstream.Requests[0].RequestUri!.Query.Should().Contain("password=example-password");
    }

    [Fact]
    public async Task A_failed_test_explains_itself()
    {
        _upstream.OnAction(null, XtreamFixtures.LapsedAccount);

        var result = await _manager.TestConnectionAsync(XtreamSettings(), existingId: null, TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("The provider account expired on 2020-09-13.");
        result.AccountStatus.Should().Be("Active");
    }

    [Fact]
    public async Task An_unknown_account_sees_no_playlists()
    {
        _users.GetUserAccountAsync(Arg.Any<Guid>()).Returns((UserVM?)null);
        _repo.GetAllPlaylistsAsync().Returns(new List<IptvPlaylist> { new() { Name = "Free", M3uUrl = "https://iptv.example.com/list.m3u" } });

        (await _manager.GetClientPlaylistsAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task Client_playlists_follow_the_account_and_profile_allowlists_and_are_redacted()
    {
        var allowed = new IptvPlaylist { Name = "Allowed", SourceType = IptvSourceType.Xtream, ServerUrl = XtreamFixtures.Server, Username = "viewer", PasswordCiphertext = "x", LastError = "boom" };
        allowed.Channels.Add(new IptvChannel { ExternalChannelId = "a", Name = "A", StreamUrl = "xtream:1.ts", PlaylistId = allowed.Id });
        var profileBlocked = new IptvPlaylist { Name = "Profile blocked", M3uUrl = "https://iptv.example.com/b.m3u" };
        var accountBlocked = new IptvPlaylist { Name = "Account blocked", M3uUrl = "https://iptv.example.com/c.m3u" };
        _repo.GetAllPlaylistsAsync().Returns(new List<IptvPlaylist> { allowed, profileBlocked, accountBlocked });
        var profileId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        _users.GetUserAccountAsync(accountId).Returns(new UserVM
        {
            Id = accountId,
            HasAllIptvAccess = false,
            AllowedIptvPlaylistIds = new List<Guid> { allowed.Id, profileBlocked.Id },
            Profiles = new List<UserProfileVM> { new() { Id = profileId, HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { allowed.Id } } }
        });

        var playlists = await _manager.GetClientPlaylistsAsync(accountId, profileId);

        playlists.Should().ContainSingle();
        playlists[0].Name.Should().Be("Allowed");
        playlists[0].M3uUrl.Should().Be("http://provider.example.com");
        playlists[0].LastError.Should().BeNull();
        playlists[0].Channels.Single().StreamUrl.Should().StartWith("vora-channel:");
    }

    [Fact]
    public async Task Timeshift_refuses_a_channel_on_a_playlist_the_profile_may_not_watch()
    {
        var playlistId = Guid.NewGuid();
        var channel = new IptvChannel { ExternalChannelId = "a", Name = "A", StreamUrl = "https://iptv.example.com/a.ts", PlaylistId = playlistId };
        _repo.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _users.GetUserAccountAsync(accountId).Returns(new UserVM
        {
            Id = accountId,
            HasAllIptvAccess = true,
            Profiles = new List<UserProfileVM> { new() { Id = profileId, HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid>() } }
        });

        var act = async () => await _manager.StartTimeshiftSessionAsync(channel.Id, accountId, profileId, null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _timeshift.DidNotReceiveWithAnyArgs().LockProfileAsync(default);
    }

    [Fact]
    public async Task A_live_remux_waits_for_a_free_connection_like_any_other_stream()
    {
        var playlistId = Guid.NewGuid();
        var channel = new IptvChannel { ExternalChannelId = "a", Name = "A", StreamUrl = "https://iptv.example.com/a.ts", PlaylistId = playlistId };
        _tuners.TryAcquire(playlistId, 1, IptvManager.TimeshiftLeaseKey(Guid.NewGuid()), TunerLeaseKind.Timeshift).Should().BeTrue();

        var act = async () => await _manager.StartLiveRemuxAsync(channel, new IptvUpstream(channel.StreamUrl, IptvRequestPolicy.None), Guid.NewGuid(), connectionLimit: 1);

        await act.Should().ThrowAsync<TunerLimitReachedException>();
        _tuners.ActiveCount(playlistId).Should().Be(1);
    }

    [Fact]
    public async Task Timeshift_takes_over_the_viewer_s_passthrough_connection_on_that_playlist()
    {
        var playlistId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var channel = new IptvChannel { ExternalChannelId = "a", Name = "A", StreamUrl = "https://iptv.example.com/a.ts", PlaylistId = playlistId };
        _repo.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        _users.GetUserAccountAsync(accountId).Returns(new UserVM { Id = accountId, HasAllIptvAccess = true, Profiles = new List<UserProfileVM> { new() { Id = profileId, HasAllIptvAccess = true } } });
        _tuners.TryAcquire(playlistId, 1, IptvPassthroughService.LiveLeaseKey($"{profileId:N}.{playlistId:N}.{Guid.NewGuid():N}"), TunerLeaseKind.Live);
        _tuners.TryAcquire(playlistId, 0, IptvPassthroughService.LiveLeaseKey($"{Guid.NewGuid():N}.{playlistId:N}.{Guid.NewGuid():N}"), TunerLeaseKind.Live);
        _timeshift.TryRegister(default, default!, default!, default).ReturnsForAnyArgs(false);
        var root = Path.Combine(Path.GetTempPath(), "vora-takeover-" + Guid.NewGuid().ToString("N"));
        _repo.GetServerSettingsAsync().Returns(new ServerSetting { TranscoderTempDirectory = root });

        try
        {
            await _manager.StartTimeshiftSessionAsync(channel.Id, accountId, profileId, null);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        _tuners.ActiveCount(playlistId).Should().Be(1);
    }

    [Fact]
    public async Task Stopping_a_viewer_s_remux_leaves_a_timeshift_alone()
    {
        var viewer = Guid.NewGuid();
        var playlistId = Guid.NewGuid();
        _tuners.TryAcquire(playlistId, 0, IptvManager.TimeshiftLeaseKey(viewer), TunerLeaseKind.Timeshift);
        _timeshift.KindOf(viewer).Returns(LiveSessionKind.Timeshift);

        await _manager.StopLiveRemuxAsync(viewer);

        await _timeshift.DidNotReceive().StopAsync(viewer);
        _tuners.ActiveCount(playlistId).Should().Be(1);
    }

    [Fact]
    public async Task Stopping_a_viewer_s_remux_ends_it_and_frees_its_connection()
    {
        var viewer = Guid.NewGuid();
        var playlistId = Guid.NewGuid();
        _tuners.TryAcquire(playlistId, 0, IptvManager.TimeshiftLeaseKey(viewer), TunerLeaseKind.Timeshift);
        _timeshift.KindOf(viewer).Returns(LiveSessionKind.Remux);

        await _manager.StopLiveRemuxAsync(viewer);

        await _timeshift.Received(1).StopAsync(viewer);
        _tuners.ActiveCount(playlistId).Should().Be(0);
    }

    [Fact]
    public async Task Timeshift_refuses_a_profile_that_is_not_the_caller_s()
    {
        var channel = new IptvChannel { ExternalChannelId = "a", Name = "A", StreamUrl = "https://iptv.example.com/a.ts", PlaylistId = Guid.NewGuid() };
        _repo.GetChannelWithPlaylistAsync(channel.Id).Returns(channel);
        var accountId = Guid.NewGuid();
        _users.GetUserAccountAsync(accountId).Returns(new UserVM { Id = accountId, HasAllIptvAccess = true });

        var act = async () => await _manager.StartTimeshiftSessionAsync(channel.Id, accountId, Guid.NewGuid(), null);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
