using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Vora.Api.Hubs;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Api.Tests.Hubs;

public class VoraHubTests
{
    private readonly IUserRepository _users;
    private readonly IGroupManager _groups;
    private readonly HubCallerContext _context;
    private readonly VoraHub _hub;

    private const string TestConnectionId = "test-connection-1";

    public VoraHubTests()
    {
        _users = Substitute.For<IUserRepository>();
        _groups = Substitute.For<IGroupManager>();
        _context = Substitute.For<HubCallerContext>();
        _context.ConnectionId.Returns(TestConnectionId);

        _hub = new VoraHub(_users)
        {
            Context = _context,
            Groups = _groups
        };
    }

    private void SetUser(Guid? accountId, Guid? profileId, bool isAdmin = false)
    {
        var claims = new List<Claim>();
        if (accountId.HasValue) claims.Add(new Claim("accountId", accountId.Value.ToString()));
        if (profileId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, profileId.Value.ToString()));
        if (isAdmin) claims.Add(new Claim("isAdmin", "true"));

        _context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims)));
    }

    [Fact]
    public void UserGroupName_uses_n_format_guid()
    {
        var id = Guid.Parse("12345678-1234-5678-1234-567812345678");

        VoraHub.UserGroupName(id).Should().Be("user-12345678123456781234567812345678");
    }

    [Fact]
    public void ProfileGroupName_uses_n_format_guid()
    {
        var id = Guid.Parse("12345678-1234-5678-1234-567812345678");

        VoraHub.ProfileGroupName(id).Should().Be("profile-12345678123456781234567812345678");
    }

    [Fact]
    public void AdminGroupName_is_stable_constant()
    {
        VoraHub.AdminGroupName.Should().Be("admins");
    }

    [Fact]
    public async Task OnConnectedAsync_adds_user_group_for_authenticated_account()
    {
        var accountId = Guid.NewGuid();
        SetUser(accountId, profileId: null);
        _users.GetUserByIdAsync(accountId).Returns((User?)null);

        await _hub.OnConnectedAsync();

        await _groups.Received(1).AddToGroupAsync(TestConnectionId, VoraHub.UserGroupName(accountId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_adds_admin_group_only_when_user_is_admin()
    {
        var accountId = Guid.NewGuid();
        SetUser(accountId, profileId: null);
        _users.GetUserByIdAsync(accountId).Returns(new User
        {
            Id = accountId,
            Email = "a@b.com",
            DisplayName = "Admin",
            PasswordHash = "x",
            IsAdmin = true
        });

        await _hub.OnConnectedAsync();

        await _groups.Received(1).AddToGroupAsync(TestConnectionId, VoraHub.AdminGroupName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_does_not_add_admin_group_for_non_admin_user()
    {
        var accountId = Guid.NewGuid();
        SetUser(accountId, profileId: null);
        _users.GetUserByIdAsync(accountId).Returns(new User
        {
            Id = accountId,
            Email = "a@b.com",
            DisplayName = "User",
            PasswordHash = "x",
            IsAdmin = false
        });

        await _hub.OnConnectedAsync();

        await _groups.DidNotReceive().AddToGroupAsync(TestConnectionId, VoraHub.AdminGroupName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_adds_profile_group_when_profile_differs_from_account()
    {
        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        SetUser(accountId, profileId);
        _users.GetUserByIdAsync(accountId).Returns((User?)null);

        await _hub.OnConnectedAsync();

        await _groups.Received(1).AddToGroupAsync(TestConnectionId, VoraHub.ProfileGroupName(profileId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_does_not_add_profile_group_when_profile_equals_account()
    {
        var sharedId = Guid.NewGuid();
        // Legacy token shape: only NameIdentifier claim, no accountId — both helpers return the same guid.
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sharedId.ToString()) };
        _context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims)));
        _users.GetUserByIdAsync(sharedId).Returns((User?)null);

        await _hub.OnConnectedAsync();

        await _groups.DidNotReceive().AddToGroupAsync(TestConnectionId, VoraHub.ProfileGroupName(sharedId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_no_op_when_no_user()
    {
        _context.User.Returns((ClaimsPrincipal?)null);

        await _hub.OnConnectedAsync();

        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task OnDisconnectedAsync_removes_user_and_admin_groups()
    {
        var accountId = Guid.NewGuid();
        SetUser(accountId, profileId: null);

        await _hub.OnDisconnectedAsync(exception: null);

        await _groups.Received(1).RemoveFromGroupAsync(TestConnectionId, VoraHub.UserGroupName(accountId), Arg.Any<CancellationToken>());
        await _groups.Received(1).RemoveFromGroupAsync(TestConnectionId, VoraHub.AdminGroupName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnectedAsync_removes_profile_group_when_profile_differs_from_account()
    {
        var accountId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        SetUser(accountId, profileId);

        await _hub.OnDisconnectedAsync(exception: null);

        await _groups.Received(1).RemoveFromGroupAsync(TestConnectionId, VoraHub.ProfileGroupName(profileId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDisconnectedAsync_no_op_when_no_user()
    {
        _context.User.Returns((ClaimsPrincipal?)null);

        await _hub.OnDisconnectedAsync(exception: null);

        await _groups.DidNotReceiveWithAnyArgs().RemoveFromGroupAsync(default!, default!, default);
    }
}
