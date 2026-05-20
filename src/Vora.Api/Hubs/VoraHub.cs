using Microsoft.AspNetCore.SignalR;
using Vora.Api.Extensions;

namespace Vora.Api.Hubs;

public class VoraHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsAdmin() == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.User?.IsAdmin() == true)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");
        }
        await base.OnDisconnectedAsync(exception);
    }
}
