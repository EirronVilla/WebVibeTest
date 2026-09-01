using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public interface IGameService
{
    Task<Game> CreateGameAsync(string userId, string name, int maxPlayers, bool isPrivate, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPublicGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
    Task<GamePlayer> JoinPrivateGameAsync(string userId, string joinCode, CancellationToken cancellationToken = default);
    Task LeaveWaitingGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default);
}
