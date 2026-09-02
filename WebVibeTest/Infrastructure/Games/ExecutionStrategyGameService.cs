using Microsoft.EntityFrameworkCore;
using WebVibeTest.Application.Games;
using WebVibeTest.Domain.Games;
using WebVibeTest.Infrastructure.Data;

namespace WebVibeTest.Infrastructure.Games;

/// <summary>
/// Runs transactional game commands as complete retriable units. Npgsql's retrying
/// execution strategy cannot safely retry a transaction that was started outside it.
/// </summary>
public sealed class ExecutionStrategyGameService(
    ApplicationDbContext dbContext,
    GameService inner) : IGameService
{
    public Task<IReadOnlyList<AvailableGame>> GetAvailablePublicGamesAsync(string userId, CancellationToken cancellationToken = default) =>
        inner.GetAvailablePublicGamesAsync(userId, cancellationToken);

    public Task<WaitingLobby> GetWaitingLobbyAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        inner.GetWaitingLobbyAsync(userId, gameId, cancellationToken);

    public Task SelectPlayerColorAsync(string userId, Guid gameId, PlayerColor color, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.SelectPlayerColorAsync(userId, gameId, color, cancellationToken), cancellationToken);

    public Task StartGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.StartGameAsync(userId, gameId, cancellationToken), cancellationToken);

    public Task<ActiveGameReadModel> GetActiveGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        inner.GetActiveGameAsync(userId, gameId, cancellationToken);

    public Task<CompletedGameReadModel> GetCompletedGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        inner.GetCompletedGameAsync(userId, gameId, cancellationToken);

    public Task<CompletedGameReadModel> GetCompletedGamePublicAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        inner.GetCompletedGamePublicAsync(gameId, cancellationToken);

    public Task<UserStatistics> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default) =>
        inner.GetStatisticsAsync(userId, cancellationToken);

    public Task<bool> IsCompletedAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        inner.IsCompletedAsync(gameId, cancellationToken);

    public Task PlaceInitialSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlaceInitialSettlementAsync(userId, gameId, vertexId, cancellationToken), cancellationToken);

    public Task PlaceInitialRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlaceInitialRoadAsync(userId, gameId, edgeId, cancellationToken), cancellationToken);

    public Task<DiceRollResult> RollDiceAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.RollDiceAsync(userId, gameId, cancellationToken), cancellationToken);

    public Task DiscardResourcesAsync(string userId, Guid gameId, ResourceDiscard discard, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.DiscardResourcesAsync(userId, gameId, discard, cancellationToken), cancellationToken);

    public Task<RobberMoveResult> MoveRobberAsync(string userId, Guid gameId, int hexId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.MoveRobberAsync(userId, gameId, hexId, cancellationToken), cancellationToken);

    public Task<RobberyResult> RobPlayerAsync(string userId, Guid gameId, string targetUserId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.RobPlayerAsync(userId, gameId, targetUserId, cancellationToken), cancellationToken);

    public Task<TurnChangeResult> EndTurnAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.EndTurnAsync(userId, gameId, cancellationToken), cancellationToken);

    public Task<BuildResult> BuildRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.BuildRoadAsync(userId, gameId, edgeId, cancellationToken), cancellationToken);

    public Task<BuildResult> BuildSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.BuildSettlementAsync(userId, gameId, vertexId, cancellationToken), cancellationToken);

    public Task<BuildResult> BuildCityAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.BuildCityAsync(userId, gameId, vertexId, cancellationToken), cancellationToken);

    public Task<bool> CanAccessGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        inner.CanAccessGameAsync(userId, gameId, cancellationToken);

    public Task<TradeEventResult> ProposeTradeAsync(string userId, Guid gameId, ResourceBundle offered, ResourceBundle requested, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.ProposeTradeAsync(userId, gameId, offered, requested, cancellationToken), cancellationToken);

    public Task<TradeEventResult> RespondToTradeAsync(string userId, Guid gameId, Guid offerId, bool accept, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.RespondToTradeAsync(userId, gameId, offerId, accept, cancellationToken), cancellationToken);

    public Task<TradeEventResult> FinalizeTradeAsync(string userId, Guid gameId, Guid offerId, string acceptingUserId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.FinalizeTradeAsync(userId, gameId, offerId, acceptingUserId, cancellationToken), cancellationToken);

    public Task<TradeEventResult> CancelTradeAsync(string userId, Guid gameId, Guid offerId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.CancelTradeAsync(userId, gameId, offerId, cancellationToken), cancellationToken);

    public Task<MaritimeTradeResult> MaritimeTradeAsync(string userId, Guid gameId, ResourceType give, ResourceType receive, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.MaritimeTradeAsync(userId, gameId, give, receive, cancellationToken), cancellationToken);

    public Task<DevelopmentCardPurchaseResult> BuyDevelopmentCardAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.BuyDevelopmentCardAsync(userId, gameId, cancellationToken), cancellationToken);

    public Task<DevelopmentCardPlayResult> PlayKnightAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlayKnightAsync(userId, gameId, cardId, cancellationToken), cancellationToken);

    public Task<DevelopmentCardPlayResult> PlayRoadBuildingAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlayRoadBuildingAsync(userId, gameId, cardId, cancellationToken), cancellationToken);

    public Task<DevelopmentCardPlayResult> PlayYearOfPlentyAsync(string userId, Guid gameId, Guid cardId, ResourceType first, ResourceType second, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlayYearOfPlentyAsync(userId, gameId, cardId, first, second, cancellationToken), cancellationToken);

    public Task<DevelopmentCardPlayResult> PlayMonopolyAsync(string userId, Guid gameId, Guid cardId, ResourceType resource, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.PlayMonopolyAsync(userId, gameId, cardId, resource, cancellationToken), cancellationToken);

    public Task<BuildResult> BuildFreeRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.BuildFreeRoadAsync(userId, gameId, edgeId, cancellationToken), cancellationToken);

    public Task FinishRoadBuildingAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.FinishRoadBuildingAsync(userId, gameId, cancellationToken), cancellationToken);

    // Creation has no user-initiated transaction, so SaveChanges uses Npgsql's
    // configured execution strategy directly.
    public Task<Game> CreateGameAsync(string userId, string name, int maxPlayers, bool isPrivate, CancellationToken cancellationToken = default) =>
        inner.CreateGameAsync(userId, name, maxPlayers, isPrivate, cancellationToken);

    public Task<GamePlayer> JoinPublicGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.JoinPublicGameAsync(userId, gameId, cancellationToken), cancellationToken);

    public Task<GamePlayer> JoinPrivateGameAsync(string userId, string joinCode, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.JoinPrivateGameAsync(userId, joinCode, cancellationToken), cancellationToken);

    public Task LeaveWaitingGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.LeaveWaitingGameAsync(userId, gameId, cancellationToken), cancellationToken);

    private async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            operation,
            async (_, command, _) =>
            {
                // A failed attempt can leave tracked entities in an indeterminate state.
                // Every command reloads its authoritative state from the database.
                dbContext.ChangeTracker.Clear();
                await command();
                return true;
            },
            verifySucceeded: null,
            cancellationToken);
    }

    private Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            operation,
            async (_, command, _) =>
            {
                dbContext.ChangeTracker.Clear();
                return await command();
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
