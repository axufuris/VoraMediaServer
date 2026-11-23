using Microsoft.AspNetCore.SignalR;
using Vora.Api.Hubs;

namespace Vora.Api.Tests.Hubs;

public class SignalRClientNotifierTests
{
    private readonly IHubClients _clients = Substitute.For<IHubClients>();
    private readonly IClientProxy _proxy = Substitute.For<IClientProxy>();
    private readonly SignalRClientNotifier _notifier;

    public SignalRClientNotifierTests()
    {
        _clients.Group(Arg.Any<string>()).Returns(_proxy);
        _clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(_proxy);
        _clients.All.Returns(_proxy);

        var hub = Substitute.For<IHubContext<VoraHub>>();
        hub.Clients.Returns(_clients);
        _notifier = new SignalRClientNotifier(hub);
    }

    private void ReachedOnly(string group, string method, string payload)
    {
        _clients.Received(1).Group(group);
        _clients.DidNotReceive().Group(VoraHub.AdminGroupName);
        _clients.DidNotReceive().Groups(Arg.Any<IReadOnlyList<string>>());
        _ = _clients.DidNotReceive().All;
        _proxy.Received(1).SendCoreAsync(
            method,
            Arg.Is<object?[]>(args => args.Length == 1 && (string?)args[0] == payload),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_users_access_change_reaches_only_that_account_and_not_the_admins()
    {
        var userId = Guid.NewGuid();

        await _notifier.NotifyUserAccessUpdatedAsync(userId);

        ReachedOnly(VoraHub.UserGroupName(userId), "UserAccessUpdated", userId.ToString());
    }

    [Fact]
    public async Task A_profiles_access_change_reaches_only_that_profile_and_not_the_admins()
    {
        var profileId = Guid.NewGuid();

        await _notifier.NotifyProfileAccessUpdatedAsync(profileId);

        ReachedOnly(VoraHub.ProfileGroupName(profileId), "ProfileAccessUpdated", profileId.ToString());
    }

    [Fact]
    public async Task A_profiles_new_mixes_reach_only_that_profile_and_not_the_admins()
    {
        var profileId = Guid.NewGuid();

        await _notifier.NotifyMusicMixesUpdatedAsync(profileId);

        ReachedOnly(VoraHub.ProfileGroupName(profileId), "MusicMixesUpdated", profileId.ToString());
    }
}
