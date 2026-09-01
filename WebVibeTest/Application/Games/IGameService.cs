using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public interface IGameService
{
    Task<IReadOnlyList<AvailableGame>> GetAvailablePublicGamesAsync(string userId, CancellationToken cancellationToken = default);
    Task<WaitingLobby> GetWaitingLobbyAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task StartGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<ActiveGameReadModel> GetActiveGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task PlaceInitialSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default);
    Task PlaceInitialRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default);
    Task<DiceRollResult> RollDiceAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task DiscardResourcesAsync(string userId, Guid gameId, ResourceDiscard discard, CancellationToken cancellationToken = default);
    Task<RobberMoveResult> MoveRobberAsync(string userId, Guid gameId, int hexId, CancellationToken cancellationToken = default);
    Task<RobberyResult> RobPlayerAsync(string userId, Guid gameId, string targetUserId, CancellationToken cancellationToken = default);
    Task<TurnChangeResult> EndTurnAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<Game> CreateGameAsync(string userId, string name, int maxPlayers, bool isPrivate, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPublicGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPrivateGameAsync(string userId, string joinCode, CancellationToken cancellationToken = default);
    Task LeaveWaitingGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
}
