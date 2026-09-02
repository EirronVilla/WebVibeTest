using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public interface IGameService
{
    Task<IReadOnlyList<AvailableGame>> GetAvailablePublicGamesAsync(string userId, CancellationToken cancellationToken = default);
    Task<WaitingLobby> GetWaitingLobbyAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task StartGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<ActiveGameReadModel> GetActiveGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<CompletedGameReadModel> GetCompletedGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<CompletedGameReadModel> GetCompletedGamePublicAsync(Guid gameId, CancellationToken cancellationToken = default);
    Task<UserStatistics> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsCompletedAsync(Guid gameId, CancellationToken cancellationToken = default);
    Task PlaceInitialSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default);
    Task PlaceInitialRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default);
    Task<DiceRollResult> RollDiceAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task DiscardResourcesAsync(string userId, Guid gameId, ResourceDiscard discard, CancellationToken cancellationToken = default);
    Task<RobberMoveResult> MoveRobberAsync(string userId, Guid gameId, int hexId, CancellationToken cancellationToken = default);
    Task<RobberyResult> RobPlayerAsync(string userId, Guid gameId, string targetUserId, CancellationToken cancellationToken = default);
    Task<TurnChangeResult> EndTurnAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<BuildResult> BuildRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default);
    Task<BuildResult> BuildSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default);
    Task<BuildResult> BuildCityAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default);
    Task<bool> CanAccessGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<TradeEventResult> ProposeTradeAsync(string userId, Guid gameId, ResourceBundle offered, ResourceBundle requested, CancellationToken cancellationToken = default);
    Task<TradeEventResult> RespondToTradeAsync(string userId, Guid gameId, Guid offerId, bool accept, CancellationToken cancellationToken = default);
    Task<TradeEventResult> FinalizeTradeAsync(string userId, Guid gameId, Guid offerId, string acceptingUserId, CancellationToken cancellationToken = default);
    Task<TradeEventResult> CancelTradeAsync(string userId, Guid gameId, Guid offerId, CancellationToken cancellationToken = default);
    Task<MaritimeTradeResult> MaritimeTradeAsync(string userId, Guid gameId, ResourceType give, ResourceType receive, CancellationToken cancellationToken = default);
    Task<DevelopmentCardPurchaseResult> BuyDevelopmentCardAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<DevelopmentCardPlayResult> PlayKnightAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default);
    Task<DevelopmentCardPlayResult> PlayRoadBuildingAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default);
    Task<DevelopmentCardPlayResult> PlayYearOfPlentyAsync(string userId, Guid gameId, Guid cardId, ResourceType first, ResourceType second, CancellationToken cancellationToken = default);
    Task<DevelopmentCardPlayResult> PlayMonopolyAsync(string userId, Guid gameId, Guid cardId, ResourceType resource, CancellationToken cancellationToken = default);
    Task<BuildResult> BuildFreeRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default);
    Task FinishRoadBuildingAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<Game> CreateGameAsync(string userId, string name, int maxPlayers, bool isPrivate, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPublicGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPrivateGameAsync(string userId, string joinCode, CancellationToken cancellationToken = default);
    Task LeaveWaitingGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
}
