using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TicketDeskPro.Api.Hubs;

/// <summary>
/// Real-time push channel (FRD §8). JWT-authenticated; each connection joins
/// "user:{id}" plus role groups so services can target a person or a whole queue.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue("sub");
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        if (Context.User?.IsInRole("Agent") == true || Context.User?.IsInRole("Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "role:agents");

        await base.OnConnectedAsync();
    }
}
