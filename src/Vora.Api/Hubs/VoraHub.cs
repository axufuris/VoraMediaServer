using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Vora.Api.Extensions;
using Vora.Application.Users;

namespace Vora.Api.Hubs;

[Authorize]
public class VoraHub : Hub
{
    private readonly IUserRepository _userRepository;

    public VoraHub(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public override async Task OnConnectedAsync()
    {
        var accountId = Context.User?.GetAccountId();
        var profileId = Context.User?.GetProfileId();

        if (accountId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(accountId.Value));

            var user = await _userRepository.GetUserByIdAsync(accountId.Value);
            if (user is { IsAdmin: true })
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
            }
        }

        if (profileId.HasValue && profileId.Value != accountId.GetValueOrDefault())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ProfileGroupName(profileId.Value));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = Context.User?.GetAccountId();
        var profileId = Context.User?.GetProfileId();

        if (accountId.HasValue)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroupName(accountId.Value));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AdminGroupName);
        }

        if (profileId.HasValue && profileId.Value != accountId.GetValueOrDefault())
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ProfileGroupName(profileId.Value));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public const string AdminGroupName = "admins";
    public static string UserGroupName(Guid accountId) => $"user-{accountId:N}";
    public static string ProfileGroupName(Guid profileId) => $"profile-{profileId:N}";
}
