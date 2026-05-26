using Vora.Application.Media;
using Vora.Application.Requests;
using Vora.Application.Requests.ViewModels;
using Vora.Application.Users;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Requests;

public class RequestManagerTests
{
    private readonly IRequestRepository _requests;
    private readonly IMediaRepository _media;
    private readonly IUserRepository _users;
    private readonly IRequestNotificationService _notifier;
    private readonly IServiceProvider _services;
    private readonly RequestManager _manager;

    public RequestManagerTests()
    {
        _requests = Substitute.For<IRequestRepository>();
        _media = Substitute.For<IMediaRepository>();
        _users = Substitute.For<IUserRepository>();
        _notifier = Substitute.For<IRequestNotificationService>();
        _services = Substitute.For<IServiceProvider>();

        _manager = new RequestManager(_requests, _media, _users, _notifier, _services);
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

        var expectedRelease = new DateTime(2025, 12, 25);
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

    [Fact]
    public async Task ResolveRequestAsync_ignores_empty_external_id()
    {
        await _manager.ResolveRequestAsync("", "Movie");
        await _manager.ResolveRequestAsync("   ", "Movie");

        await _requests.DidNotReceive().GetRequestAsync(Arg.Any<string>(), Arg.Any<string>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default);
    }

    [Fact]
    public async Task ResolveRequestAsync_does_nothing_when_request_missing()
    {
        _requests.GetRequestAsync("603", "Movie").Returns((MediaRequest?)null);

        await _manager.ResolveRequestAsync("603", "Movie");

        await _requests.DidNotReceive().UpdateRequestAsync(Arg.Any<MediaRequest>());
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default);
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
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRequestAvailableAsync(default!, default);
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
        await _notifier.Received(1).NotifyRequestAvailableAsync(request, mediaItemId);
    }

    [Fact]
    public async Task GetRequestStatusAsync_returns_status_as_int_when_request_found()
    {
        _requests.GetRequestAsync("603", "Movie")
            .Returns(new MediaRequest { ExternalId = "603", Type = "Movie", Status = RequestStatus.Processing });

        var result = await _manager.GetRequestStatusAsync("603", "Movie");

        result.Should().Be((int)RequestStatus.Processing);
    }

    [Fact]
    public async Task GetRequestStatusAsync_returns_null_when_request_missing()
    {
        _requests.GetRequestAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((MediaRequest?)null);

        var result = await _manager.GetRequestStatusAsync("missing", "Movie");

        result.Should().BeNull();
    }
}
