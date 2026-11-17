using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Application.Requests.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Requests;

public class RequestManagerTests
{
    private readonly IRequestRepository _requests;
    private readonly IMediaRepository _media;
    private readonly IUserRepository _users;
    private readonly IRequestNotificationService _notifier;
    private readonly IClientNotifier _clients;
    private readonly IServiceProvider _services;
    private readonly RequestManager _manager;

    public RequestManagerTests()
    {
        _requests = Substitute.For<IRequestRepository>();
        _media = Substitute.For<IMediaRepository>();
        _users = Substitute.For<IUserRepository>();
        _notifier = Substitute.For<IRequestNotificationService>();
        _clients = Substitute.For<IClientNotifier>();
        _services = Substitute.For<IServiceProvider>();

        _manager = new RequestManager(_requests, _media, _users, _notifier, _clients, NullLogger<RequestManager>.Instance, _services);
    }

    private static UserProfile NewProfile(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Tester",
        UserId = userId
    };

    private static User NewUser(bool canRequest = true, bool autoApprove = false) => new()
    {
        Email = "tester@example.com",
        DisplayName = "Tester",
        CanRequestMedia = canRequest,
        AutoApproveRequests = autoApprove
    };

    private static RequestServerVM NewServer(string mediaType, bool enabled = true) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Radarr",
        ProviderId = "radarr",
        MediaType = mediaType,
        Hostname = "localhost",
        Port = 7878,
        IsEnabled = enabled
    };

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_skips_if_media_already_in_library()
    {
        _media.MediaExistsByExternalIdAsync("603", "Movie").Returns(true);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "The Matrix", "Movie", "poster.jpg", Guid.NewGuid(), null);

        await _requests.DidNotReceive().AddRequestAsync(Arg.Any<MediaRequest>());
        await _requests.DidNotReceive().UpdateRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_skips_if_profile_missing()
    {
        var profileId = Guid.NewGuid();
        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns((UserProfile?)null);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "x", "Movie", "p", profileId, null);

        await _requests.DidNotReceive().AddRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_skips_if_user_cannot_request()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        var user = NewUser(canRequest: false);

        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(user);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "x", "Movie", "p", profileId, null);

        await _requests.DidNotReceive().AddRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_skips_if_no_enabled_server_matches_type()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        var user = NewUser();

        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(user);
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM>
        {
            NewServer("TvShow", enabled: true),
            NewServer("Movie", enabled: false)
        });

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "p", profileId, null);

        await _requests.DidNotReceive().AddRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_adds_requester_to_existing_request()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        var user = NewUser();
        var existing = new MediaRequest { ExternalId = "603", Type = "Movie", Status = RequestStatus.Pending };

        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(user);
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { NewServer("Movie") });
        _requests.GetRequestAsync("603", "Movie").Returns(existing);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "p", profileId, null);

        existing.Requesters.Should().ContainSingle(r => r.ProfileId == profileId);
        await _requests.Received(1).UpdateRequestAsync(existing);
        await _requests.DidNotReceive().AddRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_does_not_duplicate_requester_when_already_present()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        var user = NewUser();
        var existing = new MediaRequest
        {
            ExternalId = "603",
            Type = "Movie",
            Status = RequestStatus.Pending,
            Requesters = { new MediaRequestUser { ProfileId = profileId } }
        };

        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(user);
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { NewServer("Movie") });
        _requests.GetRequestAsync("603", "Movie").Returns(existing);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "p", profileId, null);

        existing.Requesters.Should().HaveCount(1);
        await _requests.DidNotReceive().UpdateRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_creates_new_request_when_none_exists()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        var user = NewUser();

        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(user);
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { NewServer("Movie") });
        _requests.GetRequestAsync("603", "Movie").Returns((MediaRequest?)null);

        var expectedRelease = new DateOnly(2025, 12, 25);
        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "poster.jpg", profileId, expectedRelease);

        await _requests.Received(1).AddRequestAsync(Arg.Is<MediaRequest>(r =>
            r.ExternalId == "603" &&
            r.ProviderId == "tmdb" &&
            r.Title == "Matrix" &&
            r.Type == "Movie" &&
            r.PosterUrl == "poster.jpg" &&
            r.Status == RequestStatus.Pending &&
            r.ExpectedReleaseDate == expectedRelease &&
            r.Requesters.Any(u => u.ProfileId == profileId)));
    }

    private void GivenAWatchlistedMovieNobodyHasRequested(Guid profileId)
    {
        var userId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(NewUser());
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { NewServer("Movie") });
        _requests.GetRequestAsync("603", "Movie").Returns((MediaRequest?)null);
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_tells_the_admins_when_it_creates_a_request()
    {
        var profileId = Guid.NewGuid();
        GivenAWatchlistedMovieNobodyHasRequested(profileId);

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "poster.jpg", profileId, null);

        await _clients.Received(1).NotifyRequestQueueChangedAsync();
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_does_not_tell_the_admins_when_it_only_adds_a_requester()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var profile = NewProfile(userId); profile.Id = profileId;
        _media.MediaExistsByExternalIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _users.GetProfileByIdAsync(profileId).Returns(profile);
        _users.GetUserByIdAsync(userId).Returns(NewUser());
        _requests.GetAllServersAsync().Returns(new List<RequestServerVM> { NewServer("Movie") });
        _requests.GetRequestAsync("603", "Movie").Returns(new MediaRequest { ExternalId = "603", Type = "Movie", Status = RequestStatus.Pending });

        await _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "p", profileId, null);

        await _clients.DidNotReceive().NotifyRequestQueueChangedAsync();
    }

    [Fact]
    public async Task ProcessWatchlistAdditionAsync_still_creates_the_request_when_the_admins_cannot_be_told()
    {
        var profileId = Guid.NewGuid();
        GivenAWatchlistedMovieNobodyHasRequested(profileId);
        _clients.NotifyRequestQueueChangedAsync().Returns(Task.FromException(new InvalidOperationException("hub down")));

        var act = () => _manager.ProcessWatchlistAdditionAsync("603", "tmdb", "Matrix", "Movie", "poster.jpg", profileId, null);

        await act.Should().NotThrowAsync();
        await _requests.Received(1).AddRequestAsync(Arg.Any<MediaRequest>());
    }

    [Fact]
    public async Task DeleteRequestAsync_deletes_it_and_tells_the_admins()
    {
        var id = Guid.NewGuid();

        await _manager.DeleteRequestAsync(id);

        await _requests.Received(1).DeleteRequestAsync(id);
        await _clients.Received(1).NotifyRequestQueueChangedAsync();
    }

    [Fact]
    public async Task GetPendingCountAsync_is_what_the_repository_counts()
    {
        _requests.GetPendingCountAsync().Returns(9);

        (await _manager.GetPendingCountAsync()).Should().Be(9);
    }

    private (IRequestProvider Provider, MediaRequest Request) GivenAPendingMovieRequestAndARadarr(bool radarrAccepts)
    {
        var plugin = Substitute.For<IVoraPlugin, IRequestProvider>();
        plugin.Id.Returns("radarr");
        var provider = (IRequestProvider)plugin;
        provider.SupportedMediaTypes.Returns(new[] { "Movie" });
        provider.SubmitRequestAsync(default!, default!, default!, default, default, default!, default!, default!, default)
            .ReturnsForAnyArgs(radarrAccepts);
        _services.GetService(typeof(IEnumerable<IVoraPlugin>)).Returns((IEnumerable<IVoraPlugin>)new[] { plugin });

        var request = new MediaRequest { Id = Guid.NewGuid(), ExternalId = "603", Type = "Movie", Title = "Matrix", Status = RequestStatus.Pending };
        _requests.GetRequestByIdAsync(request.Id).Returns(request);
        _requests.GetServerAsync(Arg.Any<Guid?>(), "Movie").Returns(NewServer("Movie"));
        return (provider, request);
    }

    [Fact]
    public async Task ApproveRequestAsync_tells_the_admins_once_the_provider_has_taken_it()
    {
        var (_, request) = GivenAPendingMovieRequestAndARadarr(radarrAccepts: true);

        (await _manager.ApproveRequestAsync(request.Id)).Should().BeTrue();

        request.Status.Should().Be(RequestStatus.Processing);
        await _clients.Received(1).NotifyRequestQueueChangedAsync();
    }

    [Fact]
    public async Task ApproveRequestAsync_tells_nobody_when_the_provider_refuses_it()
    {
        var (_, request) = GivenAPendingMovieRequestAndARadarr(radarrAccepts: false);

        (await _manager.ApproveRequestAsync(request.Id)).Should().BeFalse();

        request.Status.Should().Be(RequestStatus.Pending);
        await _clients.DidNotReceive().NotifyRequestQueueChangedAsync();
    }

    [Fact]
    public async Task ResolveRequestAsync_ignores_empty_external_id()
    {
        await _manager.ResolveRequestAsync("", "Movie");
        await _manager.ResolveRequestAsync("   ", "Movie");

        await _requests.DidNotReceive().GetRequestAsync(Arg.Any<string>(), Arg.Any<string>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveRequestAsync_does_nothing_when_request_missing()
    {
        _requests.GetRequestAsync("603", "Movie").Returns((MediaRequest?)null);

        await _manager.ResolveRequestAsync("603", "Movie");

        await _requests.DidNotReceive().UpdateRequestAsync(Arg.Any<MediaRequest>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(RequestStatus.Pending)]
    [InlineData(RequestStatus.Approved)]
    [InlineData(RequestStatus.Denied)]
    [InlineData(RequestStatus.Available)]
    public async Task ResolveRequestAsync_only_transitions_from_Processing(RequestStatus startingStatus)
    {
        var request = new MediaRequest { ExternalId = "603", Type = "Movie", Status = startingStatus };
        _requests.GetRequestAsync("603", "Movie").Returns(request);

        await _manager.ResolveRequestAsync("603", "Movie");

        request.Status.Should().Be(startingStatus);
        await _requests.DidNotReceive().UpdateRequestAsync(Arg.Any<MediaRequest>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveRequestAsync_transitions_processing_to_available_and_notifies()
    {
        var mediaItemId = Guid.NewGuid();
        var request = new MediaRequest { ExternalId = "603", Type = "Movie", Status = RequestStatus.Processing };
        _requests.GetRequestAsync("603", "Movie").Returns(request);

        await _manager.ResolveRequestAsync("603", "Movie", mediaItemId);

        request.Status.Should().Be(RequestStatus.Available);
        request.UpdatedAt.Should().NotBeNull();
        await _requests.Received(1).UpdateRequestAsync(request);
        await _notifier.Received(1).NotifyRequestAvailableAsync(request, mediaItemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRequestStatusAsync_returns_the_status_when_a_request_is_found()
    {
        _requests.GetRequestAsync("603", "Movie")
            .Returns(new MediaRequest { ExternalId = "603", Type = "Movie", Status = RequestStatus.Processing });

        var result = await _manager.GetRequestStatusAsync("603", "Movie");

        result.Should().Be(RequestStatus.Processing);
    }

    [Fact]
    public async Task GetRequestStatusAsync_returns_null_when_request_missing()
    {
        _requests.GetRequestAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((MediaRequest?)null);

        var result = await _manager.GetRequestStatusAsync("missing", "Movie");

        result.Should().BeNull();
    }
}
