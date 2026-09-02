using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using WebVibeTest.Application.Games;
using WebVibeTest.Infrastructure.Games;

namespace WebVibeTest.Hubs;

[Authorize]
public sealed class GameHub(IGameService gameService, InMemoryGameChat gameChat) : Hub
{
    public const string LobbyUpdatedEvent = "LobbyUpdated";
    public const string GameStartedEvent = "GameStarted";
    public const string GameStateUpdatedEvent = "GameStateUpdated";
    public const string GameCancelledEvent = "GameCancelled";
    public const string DiceRolledEvent = "DiceRolled";
    public const string ResourceProductionEvent = "ResourceProduction";
    public const string ResourceCardsReceivedEvent = "ResourceCardsReceived";
    public const string ResourceCountsChangedEvent = "ResourceCountsChanged";
    public const string RobberMovedEvent = "RobberMoved";
    public const string TurnChangedEvent = "TurnChanged";
    public const string BuildingPlacedEvent = "BuildingPlaced";
    public const string TradeOfferedEvent = "TradeOffered";
    public const string TradeRespondedEvent = "TradeResponded";
    public const string TradeCompletedEvent = "TradeCompleted";
    public const string TradeCancelledEvent = "TradeCancelled";
    public const string TradeAllRejectedEvent = "TradeAllRejected";
    public const string TradeReadyEvent = "TradeReady";
    public const string MaritimeTradeCompletedEvent = "MaritimeTradeCompleted";
    public const string DevelopmentCardBoughtEvent = "DevelopmentCardBought";
    public const string DevelopmentCardPlayedEvent = "DevelopmentCardPlayed";
    public const string AwardsChangedEvent = "AwardsChanged";
    public const string GameCompletedEvent = "GameCompleted";
    public const string PairedTurnChangedEvent = "PairedTurnChanged";
    public const string ActionLogEntryAddedEvent = "ActionLogEntryAdded";
    public const string ChatHistoryEvent = "ChatHistory";
    public const string ChatMessageEvent = "ChatMessage";

    public async Task JoinLobby(Guid gameId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await gameService.CanAccessGameAsync(userId, gameId, Context.ConnectionAborted))
            throw new HubException("Only game players may join this group.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameId));
        await Clients.Caller.SendAsync(ChatHistoryEvent, gameChat.Get(gameId), Context.ConnectionAborted);
    }

    public async Task SendChatMessage(Guid gameId, string message)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !await gameService.CanAccessGameAsync(userId, gameId, Context.ConnectionAborted))
            throw new HubException("Only game players may chat.");
        message = (message ?? string.Empty).Trim();
        if (message.Length is < 1 or > 500) throw new HubException("Messages must contain 1 to 500 characters.");
        var senderName = Context.User?.Identity?.Name?.Split('@')[0] ?? "Player";
        var entry = gameChat.Add(gameId, userId, senderName, message);
        await Clients.Group(GroupName(gameId)).SendAsync(ChatMessageEvent, entry, Context.ConnectionAborted);
    }

    public Task LeaveLobby(Guid gameId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(gameId));

    public static string GroupName(Guid gameId) => $"lobby:{gameId:N}";
}
