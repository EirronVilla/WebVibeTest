using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebVibeTest.Domain.Board;
using WebVibeTest.Domain.Games;
using WebVibeTest.Infrastructure.Data;

namespace WebVibeTest.Application.Games;

public sealed class GameService(ApplicationDbContext dbContext) : IGameService
{
    public async Task<IReadOnlyList<AvailableGame>> GetAvailablePublicGamesAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        return await dbContext.Games
            .AsNoTracking()
            .Where(game => !game.IsPrivate
                && game.Status == GameStatus.WaitingForPlayers
                && game.Players.Count < game.MaxPlayers)
            .OrderByDescending(game => game.CreatedAt)
            .Select(game => new AvailableGame(
                game.Id,
                game.Name,
                dbContext.Users.Where(user => user.Id == game.HostUserId).Select(user => user.UserName).FirstOrDefault() ?? "Unknown",
                game.Players.Count,
                game.MaxPlayers,
                game.Players.Any(player => player.UserId == userId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<WaitingLobby> GetWaitingLobbyAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        var game = await dbContext.Games
            .AsNoTracking()
            .Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new KeyNotFoundException("The game was not found.");

        if (game.Status != GameStatus.WaitingForPlayers)
        {
            throw new InvalidOperationException("This game is no longer in the waiting lobby.");
        }

        if (!game.Players.Any(player => player.UserId == userId))
        {
            throw new UnauthorizedAccessException("Only joined players may view this lobby.");
        }

        var userIds = game.Players.Select(player => player.UserId).Append(game.HostUserId).Distinct().ToList();
        var userNames = await dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? user.Email ?? user.Id, cancellationToken);

        return new WaitingLobby(
            game.Id,
            game.Name,
            userNames.GetValueOrDefault(game.HostUserId, "Unknown"),
            game.MaxPlayers,
            game.IsPrivate,
            game.JoinCode,
            game.HostUserId == userId,
            game.HostUserId == userId && game.Players.Count >= 3 && game.Players.Count <= game.MaxPlayers,
            game.Players
                .OrderBy(player => player.TurnOrder)
                .Select(player => new WaitingLobbyPlayer(
                    userNames.GetValueOrDefault(player.UserId, "Unknown"),
                    player.Color,
                    player.IsHost,
                    player.UserId == userId))
                .ToList());
    }

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

    public async Task StartGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await dbContext.Games
            .Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game was not found.");

        if (game.HostUserId != userId)
        {
            throw new UnauthorizedAccessException("Only the host may start the game.");
        }

        if (game.Status != GameStatus.WaitingForPlayers)
        {
            throw new InvalidOperationException("Only a waiting game may be started.");
        }

        if (game.Players.Count < 3 || game.Players.Count > game.MaxPlayers)
        {
            throw new InvalidOperationException($"The game requires between 3 and {game.MaxPlayers} players to start.");
        }

        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var orderedPlayers = game.Players
            .OrderBy(player => TurnOrderKey(seed, player.UserId), StringComparer.Ordinal)
            .ToList();

        // Move through unique temporary values so PostgreSQL's immediate unique index
        // cannot reject a permutation of existing turn orders.
        for (var index = 0; index < orderedPlayers.Count; index++)
        {
            orderedPlayers[index].TurnOrder = -(index + 1);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < orderedPlayers.Count; index++)
        {
            orderedPlayers[index].TurnOrder = index + 1;
        }

        game.BoardSeed = seed;
        game.BoardStateJson = SerializeBoard(BoardGenerator.Generate(seed, game.Players.Count));
        game.Status = GameStatus.InProgress;
        game.Phase = GamePhase.InitialPlacementForward;
        game.CurrentPlayerUserId = orderedPlayers[0].UserId;
        game.PendingSettlementVertexId = null;
        game.StartedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ActiveGameReadModel> GetActiveGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        var game = await dbContext.Games
            .AsNoTracking()
            .Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new KeyNotFoundException("The game was not found.");

        EnsureActiveGame(game, userId);
        var board = DeserializeBoard(game.BoardStateJson!);
        var isCurrentPlayer = game.CurrentPlayerUserId == userId;
        var validSettlements = new HashSet<int>();
        var validRoads = new HashSet<int>();

        if (isCurrentPlayer && IsInitialPlacement(game.Phase))
        {
            if (game.PendingSettlementVertexId is null)
            {
                validSettlements.UnionWith(board.Vertices
                    .Where(vertex => vertex.Settlement is null
                        && vertex.AdjacentVertexIds.All(adjacentId => board.Vertices[adjacentId].Settlement is null))
                    .Select(vertex => vertex.Id));
            }
            else
            {
                validRoads.UnionWith(board.Vertices[game.PendingSettlementVertexId.Value].EdgeIds
                    .Where(edgeId => board.Edges[edgeId].Road is null));
            }
        }

        var currentPlayerName = await dbContext.Users
            .Where(identity => identity.Id == game.CurrentPlayerUserId)
            .Select(identity => identity.UserName ?? identity.Email ?? identity.Id)
            .SingleAsync(cancellationToken);

        return new ActiveGameReadModel(
            game.Id,
            game.Name,
            game.Phase!.Value,
            currentPlayerName,
            isCurrentPlayer,
            game.PendingSettlementVertexId is null,
            board,
            validSettlements,
            validRoads);
    }

    public async Task PlaceInitialSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentInitialPlayer(game, userId);

        if (game.PendingSettlementVertexId is not null)
        {
            throw new InvalidOperationException("Place the road connected to your new settlement before placing another settlement.");
        }

        var board = DeserializeBoard(game.BoardStateJson!);
        var vertex = board.Vertices.SingleOrDefault(candidate => candidate.Id == vertexId)
            ?? throw new ArgumentException("The selected intersection does not exist.", nameof(vertexId));
        if (vertex.Settlement is not null)
        {
            throw new InvalidOperationException("That intersection already contains a building.");
        }

        if (vertex.AdjacentVertexIds.Any(adjacentId => board.Vertices[adjacentId].Settlement is not null))
        {
            throw new InvalidOperationException("Settlements must be at least two edges apart.");
        }

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        vertex.Settlement = new SettlementState { UserId = userId, Color = player.Color };
        game.PendingSettlementVertexId = vertex.Id;
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PlaceInitialRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentInitialPlayer(game, userId);

        if (game.PendingSettlementVertexId is null)
        {
            throw new InvalidOperationException("Place a settlement before placing its road.");
        }

        var board = DeserializeBoard(game.BoardStateJson!);
        var edge = board.Edges.SingleOrDefault(candidate => candidate.Id == edgeId)
            ?? throw new ArgumentException("The selected edge does not exist.", nameof(edgeId));
        if (edge.Road is not null)
        {
            throw new InvalidOperationException("That edge already contains a road.");
        }

        if (edge.VertexAId != game.PendingSettlementVertexId && edge.VertexBId != game.PendingSettlementVertexId)
        {
            throw new InvalidOperationException("The road must connect to the settlement that was just placed.");
        }

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        edge.Road = new RoadState { UserId = userId, Color = player.Color };
        game.PendingSettlementVertexId = null;
        AdvanceInitialPlacement(game);
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Game> LoadActiveGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        await dbContext.Games.Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game was not found.");

    private static void EnsureActiveGame(Game game, string userId)
    {
        if (!game.Players.Any(player => player.UserId == userId))
        {
            throw new UnauthorizedAccessException("Only players may access this game.");
        }

        if (game.Status != GameStatus.InProgress || game.Phase is null || game.BoardStateJson is null)
        {
            throw new InvalidOperationException("The game is not in progress.");
        }
    }

    private static void EnsureCurrentInitialPlayer(Game game, string userId)
    {
        if (!IsInitialPlacement(game.Phase))
        {
            throw new InvalidOperationException("Initial placement has ended.");
        }

        if (game.CurrentPlayerUserId != userId)
        {
            throw new InvalidOperationException("It is not this player's turn.");
        }
    }

    private static bool IsInitialPlacement(GamePhase? phase) =>
        phase is GamePhase.InitialPlacementForward or GamePhase.InitialPlacementReverse;

    private static void AdvanceInitialPlacement(Game game)
    {
        var players = game.Players.OrderBy(player => player.TurnOrder).ToList();
        var currentIndex = players.FindIndex(player => player.UserId == game.CurrentPlayerUserId);
        if (game.Phase == GamePhase.InitialPlacementForward)
        {
            if (currentIndex < players.Count - 1)
            {
                game.CurrentPlayerUserId = players[currentIndex + 1].UserId;
            }
            else
            {
                game.Phase = GamePhase.InitialPlacementReverse;
            }
        }
        else if (currentIndex > 0)
        {
            game.CurrentPlayerUserId = players[currentIndex - 1].UserId;
        }
        else
        {
            game.Phase = GamePhase.TurnProduction;
            game.CurrentPlayerUserId = players[0].UserId;
        }
    }

    private static string TurnOrderKey(int seed, string userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{userId}")));

    private static string SerializeBoard(BoardState board) => JsonSerializer.Serialize(board);

    private static BoardState DeserializeBoard(string json) =>
        JsonSerializer.Deserialize<BoardState>(json)
        ?? throw new InvalidOperationException("The persisted board state is invalid.");

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
        dbContext.GamePlayers.Add(player);
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
