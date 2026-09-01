using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WebVibeTest.Hubs;

[Authorize]
public sealed class GameHub : Hub
{
    public const string LobbyUpdatedEvent = "LobbyUpdated";

    public Task JoinLobby(Guid gameId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameId));

    public Task LeaveLobby(Guid gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(gameId));

    public static string GroupName(Guid gameId) => $"lobby:{gameId:N}";
}
