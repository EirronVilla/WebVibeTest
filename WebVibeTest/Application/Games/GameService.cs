using System.Data;
using Microsoft.EntityFrameworkCore;
using WebVibeTest.Domain.Games;
using WebVibeTest.Infrastructure.Data;

namespace WebVibeTest.Application.Games;

public sealed class GameService(ApplicationDbContext dbContext) : IGameService
{
    public async Task<Game> CreateGameAsync(string userId, string name, int maxPlayers, bool isPrivate, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A game name is required.", nameof(name));
        }

        if (name.Length > 200)
        {
            throw new ArgumentException("The game name cannot exceed 200 characters.", nameof(name));
        }

        if (maxPlayers is < 3 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Maximum players must be between 3 and 6.");
        }

        var now = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = GameStatus.WaitingForPlayers,
            MaxPlayers = maxPlayers,
            IsPrivate = isPrivate,
            JoinCode = isPrivate ? CreateJoinCode() : null,
            HostUserId = userId,
            CreatedAt = now
        };

        game.Players.Add(new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            UserId = userId,
            TurnOrder = 1,
            Color = PlayerColor.Red,
            IsHost = true,
            JoinedAt = now
        });

        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync(cancellationToken);
        return game;
    }

    public async Task<GamePlayer> JoinPublicGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        return await JoinAsync(
            userId,
            games => games.Where(game => game.Id == gameId && !game.IsPrivate),
            "The public game was not found.",
            cancellationToken);
    }

    public async Task<GamePlayer> JoinPrivateGameAsync(string userId, string joinCode, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            throw new ArgumentException("A join code is required.", nameof(joinCode));
        }

        var normalizedCode = joinCode.Trim().ToUpperInvariant();
        return await JoinAsync(
            userId,
            games => games.Where(game => game.IsPrivate && game.JoinCode == normalizedCode),
            "The private game was not found.",
            cancellationToken);
    }

    public async Task LeaveWaitingGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var game = await dbContext.Games
            .Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game was not found.");

        if (game.Status != GameStatus.WaitingForPlayers)
        {
            throw new InvalidOperationException("Players may only leave a game while it is waiting for players.");
        }

        var player = game.Players.SingleOrDefault(player => player.UserId == userId)
            ?? throw new InvalidOperationException("The user is not a player in this game.");

        game.Players.Remove(player);
        dbContext.GamePlayers.Remove(player);

        if (game.Players.Count == 0)
        {
            dbContext.Games.Remove(game);
        }
        else
        {
            var orderedPlayers = game.Players.OrderBy(player => player.TurnOrder).ToList();
            if (player.IsHost)
            {
                var newHost = orderedPlayers[0];
                newHost.IsHost = true;
                game.HostUserId = newHost.UserId;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<GamePlayer> JoinAsync(
        string userId,
        Func<IQueryable<Game>, IQueryable<Game>> gameFilter,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var game = await gameFilter(dbContext.Games)
            .Include(game => game.Players)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(notFoundMessage);

        if (game.Status != GameStatus.WaitingForPlayers)
        {
            throw new InvalidOperationException("The game is no longer accepting players.");
        }

        if (game.Players.Any(player => player.UserId == userId))
        {
            throw new InvalidOperationException("The user has already joined this game.");
        }

        if (game.Players.Count >= game.MaxPlayers)
        {
            throw new InvalidOperationException("The game is full.");
        }

        var usedColors = game.Players.Select(player => player.Color).ToHashSet();
        var color = Enum.GetValues<PlayerColor>().First(candidate => !usedColors.Contains(candidate));
        var usedTurnOrders = game.Players.Select(player => player.TurnOrder).ToHashSet();
        var turnOrder = Enumerable.Range(1, game.MaxPlayers).First(candidate => !usedTurnOrders.Contains(candidate));
        var player = new GamePlayer
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            UserId = userId,
            TurnOrder = turnOrder,
            Color = color,
            IsHost = false,
            JoinedAt = DateTime.UtcNow
        };

        game.Players.Add(player);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return player;
    }

    private static void EnsureAuthenticated(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("An authenticated user is required.");
        }
    }

    private static string CreateJoinCode() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
