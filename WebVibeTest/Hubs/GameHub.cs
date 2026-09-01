using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using WebVibeTest.Application.Games;

namespace WebVibeTest.Hubs;

[Authorize]
public sealed class GameHub(IGameService gameService) : Hub
{
    public const string LobbyUpdatedEvent = "LobbyUpdated";
    public const string GameStartedEvent = "GameStarted";
    public const string GameStateUpdatedEvent = "GameStateUpdated";
    public const string DiceRolledEvent = "DiceRolled";
    public const string ResourceProductionEvent = "ResourceProduction";
    public const string ResourceCountsChangedEvent = "ResourceCountsChanged";
    public const string RobberMovedEvent = "RobberMoved";
    public const string TurnChangedEvent = "TurnChanged";
    public const string BuildingPlacedEvent = "BuildingPlaced";
    public const string TradeOfferedEvent = "TradeOffered";
    public const string TradeRespondedEvent = "TradeResponded";
    public const string TradeCompletedEvent = "TradeCompleted";
    public const string TradeCancelledEvent = "TradeCancelled";
    public const string MaritimeTradeCompletedEvent = "MaritimeTradeCompleted";

    public async Task JoinLobby(Guid gameId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await gameService.CanAccessGameAsync(userId, gameId, Context.ConnectionAborted))
            throw new HubException("Only game players may join this group.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameId));
    }

    public Task LeaveLobby(Guid gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(gameId));

    public static string GroupName(Guid gameId) => $"lobby:{gameId:N}";
}
