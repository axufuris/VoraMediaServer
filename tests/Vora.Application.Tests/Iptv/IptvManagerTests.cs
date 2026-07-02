using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Iptv;
using Vora.Application.Streaming;
using Vora.Application.Tasks;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class IptvManagerTests
{
    // NOTE: AddPlaylistAsync / RefreshPlaylistAsync also call SyncM3uChannelsAsync
    // which fetches the m3u file over HTTP, so those paths aren't exercised here
    // (they need a real or stubbed HttpClient). Tests cover the management
    // operations and EPG-source CRUD which are pure repository delegations.

    private readonly IIptvRepository _repo;
    private readonly IIptvEpgService _epg;
    private readonly IUserManager _users;
    private readonly ITaskQueueManager _tasks;
    private readonly ITimeshiftCoordinator _timeshift;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IStreamingTokenSigner _signer;
    private readonly IptvManager _manager;

    public IptvManagerTests()
    {
        _repo = Substitute.For<IIptvRepository>();
        _epg = Substitute.For<IIptvEpgService>();
        _users = Substitute.For<IUserManager>();
        _tasks = Substitute.For<ITaskQueueManager>();
        _timeshift = Substitute.For<ITimeshiftCoordinator>();
        _tunerRegistry = new TunerRegistry();
        _httpFactory = Substitute.For<IHttpClientFactory>();
        _signer = Substitute.For<IStreamingTokenSigner>();

        _manager = new IptvManager(_repo, _epg, _users, _tasks, _timeshift, _tunerRegistry, _httpFactory, _signer,
            NullLogger<IptvManager>.Instance);
    }

    [Fact]
    public async Task ToggleChannelVisibilityAsync_delegates_to_repository()
    {
        var channelId = Guid.NewGuid();

        await _manager.ToggleChannelVisibilityAsync(channelId);

        await _repo.Received(1).ToggleChannelVisibilityAsync(channelId);
    }

    [Theory]
    [InlineData(IptvChannelKind.Tv)]
    [InlineData(IptvChannelKind.Radio)]
    public async Task SetChannelKindAsync_delegates_to_repository(IptvChannelKind kind)
    {
        var channelId = Guid.NewGuid();

        await _manager.SetChannelKindAsync(channelId, kind);

        await _repo.Received(1).SetChannelKindAsync(channelId, kind);
    }

    [Fact]
    public async Task DeletePlaylistAsync_no_op_when_playlist_missing()
    {
        var id = Guid.NewGuid();
        _repo.GetPlaylistByIdAsync(id).Returns((IptvPlaylist?)null);

        await _manager.DeletePlaylistAsync(id);

        await _repo.DidNotReceive().DeletePlaylistAsync(id);
        await _epg.DidNotReceiveWithAnyArgs().RemoveChannelsFromCacheAsync(default!);
    }

    [Fact]
    public async Task DeletePlaylistAsync_deletes_playlist_and_removes_channels_from_epg_cache()
    {
        var id = Guid.NewGuid();
        var playlist = new IptvPlaylist
        {
            Id = id,
            Name = "p",
            Channels = new List<IptvChannel>
            {
                new() { Id = Guid.NewGuid(), ExternalChannelId = "ch1", Name = "n1", StreamUrl = "u1" },
                new() { Id = Guid.NewGuid(), ExternalChannelId = "ch2", Name = "n2", StreamUrl = "u2" }
            }
        };
        _repo.GetPlaylistByIdAsync(id).Returns(playlist);

        await _manager.DeletePlaylistAsync(id);

        await _repo.Received(1).DeletePlaylistAsync(id);
        await _epg.Received(1).RemoveChannelsFromCacheAsync(Arg.Is<List<string>>(l => l.SequenceEqual(new[] { "ch1", "ch2" })));
    }

    [Fact]
    public async Task GetAllPlaylistsAsync_passes_kind_filter_to_repository()
    {
        _repo.GetAllPlaylistsAsync(IptvChannelKind.Radio).Returns(new List<IptvPlaylist>());

        await _manager.GetAllPlaylistsAsync(IptvChannelKind.Radio);

        await _repo.Received(1).GetAllPlaylistsAsync(IptvChannelKind.Radio);
    }

    [Fact]
    public async Task GetAllPlaylistsAsync_returns_view_models_for_each_playlist()
    {
        _repo.GetAllPlaylistsAsync(Arg.Any<IptvChannelKind?>()).Returns(new List<IptvPlaylist>
        {
            new() { Id = Guid.NewGuid(), Name = "first" },
            new() { Id = Guid.NewGuid(), Name = "second" }
        });

        var result = await _manager.GetAllPlaylistsAsync();

        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().BeEquivalentTo(new[] { "first", "second" });
    }

    [Fact]
    public async Task AddEpgSourceAsync_persists_and_kicks_off_sync()
    {
        _repo.GetEpgSourceByIdAsync(Arg.Any<Guid>())
            .Returns(c => new IptvEpgSource { Id = c.Arg<Guid>(), Name = "n", XmlTvUrl = "u" });

        var vm = await _manager.AddEpgSourceAsync("My EPG", "https://example.com/epg.xml", priority: 1);

        await _repo.Received(1).AddEpgSourceAsync(Arg.Is<IptvEpgSource>(s =>
            s.Name == "My EPG" &&
            s.XmlTvUrl == "https://example.com/epg.xml" &&
            s.Priority == 1 &&
            s.IsActive));
        await _epg.Received(1).SyncEpgDataAsync(Arg.Any<CancellationToken>());
        vm.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateEpgSourceAsync_throws_when_source_not_found()
    {
        _repo.GetEpgSourceByIdAsync(Arg.Any<Guid>()).Returns((IptvEpgSource?)null);

        var act = async () => await _manager.UpdateEpgSourceAsync(Guid.NewGuid(), "n", "u", priority: 0, isActive: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task UpdateEpgSourceAsync_writes_back_changed_fields()
    {
        var id = Guid.NewGuid();
        var source = new IptvEpgSource { Id = id, Name = "old", XmlTvUrl = "old-url", Priority = 0, IsActive = true };
        _repo.GetEpgSourceByIdAsync(id).Returns(source);

        await _manager.UpdateEpgSourceAsync(id, "new", "new-url", priority: 5, isActive: false);

        source.Name.Should().Be("new");
        source.XmlTvUrl.Should().Be("new-url");
        source.Priority.Should().Be(5);
        source.IsActive.Should().BeFalse();
        await _repo.Received(1).UpdateEpgSourceAsync(source);
    }

    [Fact]
    public async Task DeleteEpgSourceAsync_deletes_then_triggers_re_sync()
    {
        var id = Guid.NewGuid();

        await _manager.DeleteEpgSourceAsync(id);

        await _repo.Received(1).DeleteEpgSourceAsync(id);
        await _epg.Received(1).SyncEpgDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshEpgSourceAsync_just_triggers_full_sync()
    {
        await _manager.RefreshEpgSourceAsync(Guid.NewGuid());

        await _epg.Received(1).SyncEpgDataAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePlaylistAsync_throws_when_not_found()
    {
        _repo.GetPlaylistByIdAsync(Arg.Any<Guid>()).Returns((IptvPlaylist?)null);

        var act = async () => await _manager.UpdatePlaylistAsync(Guid.NewGuid(), "n", "u", true, 2, true, IptvChannelKind.Tv);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Playlist not found*");
    }

    [Fact]
    public async Task UpdatePlaylistAsync_creates_tuner_profile_when_missing()
    {
        var id = Guid.NewGuid();
        var playlist = new IptvPlaylist { Id = id, Name = "old", M3uUrl = "same", TunerProfile = null };
        _repo.GetPlaylistByIdAsync(id).Returns(playlist);

        await _manager.UpdatePlaylistAsync(id, "new", "same", supportsWebPlayback: true,
            maxConcurrentStreams: 4, isActive: true, defaultKind: IptvChannelKind.Tv);

        playlist.TunerProfile.Should().NotBeNull();
        playlist.TunerProfile!.MaxConcurrentStreams.Should().Be(4);
    }

    [Fact]
    public async Task UpdatePlaylistAsync_updates_existing_tuner_profile_max_streams()
    {
        var id = Guid.NewGuid();
        var playlist = new IptvPlaylist
        {
            Id = id,
            Name = "p",
            M3uUrl = "same",
            TunerProfile = new IptvTunerProfile { PlaylistId = id, MaxConcurrentStreams = 1 }
        };
        _repo.GetPlaylistByIdAsync(id).Returns(playlist);

        await _manager.UpdatePlaylistAsync(id, "p", "same", supportsWebPlayback: true,
            maxConcurrentStreams: 8, isActive: true, defaultKind: playlist.DefaultChannelKind);

        playlist.TunerProfile!.MaxConcurrentStreams.Should().Be(8);
        // No m3u change, no kind change → no EPG queue
        _tasks.DidNotReceive().QueueIptvEpgSync();
    }
}
