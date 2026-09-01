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
        var availableConstructionVertices = board.Vertices
            .Where(vertex => vertex.Settlement is null
                && vertex.AdjacentVertexIds.All(adjacentId => board.Vertices[adjacentId].Settlement is null))
            .Select(vertex => vertex.Id)
            .ToHashSet();
        var validSettlements = new HashSet<int>();
        var validRoads = new HashSet<int>();

        if (isCurrentPlayer && IsInitialPlacement(game.Phase))
        {
            if (game.PendingSettlementVertexId is null)
            {
                validSettlements.UnionWith(availableConstructionVertices);
            }
            else
            {
                validRoads.UnionWith(board.Vertices[game.PendingSettlementVertexId.Value].EdgeIds
                    .Where(edgeId => board.Edges[edgeId].Road is null));
            }
        }

        var playerUserIds = game.Players.Select(player => player.UserId).ToList();
        var userNames = await dbContext.Users
            .Where(identity => playerUserIds.Contains(identity.Id))
            .ToDictionaryAsync(identity => identity.Id, identity => identity.UserName ?? identity.Email ?? identity.Id, cancellationToken);
        var ownPlayer = game.Players.Single(player => player.UserId == userId);
        var ownRoadCount = board.Edges.Count(edge => edge.Road?.UserId == userId);
        var ownSettlementCount = board.Vertices.Count(vertex => vertex.Settlement?.UserId == userId
            && vertex.Settlement.BuildingType == BuildingType.Settlement);
        var ownCityCount = board.Vertices.Count(vertex => vertex.Settlement?.UserId == userId
            && vertex.Settlement.BuildingType == BuildingType.City);
        var canConstruct = isCurrentPlayer && game.Phase == GamePhase.TurnActions;
        var construction = new ConstructionReadModel(
            15 - ownRoadCount,
            5 - ownSettlementCount,
            4 - ownCityCount,
            ownPlayer.Brick >= 1 && ownPlayer.Lumber >= 1,
            ownPlayer.Brick >= 1 && ownPlayer.Lumber >= 1 && ownPlayer.Wool >= 1 && ownPlayer.Grain >= 1,
            ownPlayer.Ore >= 3 && ownPlayer.Grain >= 2,
            canConstruct && ownRoadCount < 15 ? GetValidRoadBuildEdges(board, userId) : new HashSet<int>(),
            canConstruct && ownSettlementCount < 5 ? GetValidSettlementBuildVertices(board, userId) : new HashSet<int>(),
            canConstruct && ownCityCount < 4 ? GetValidCityBuildVertices(board, userId) : new HashSet<int>());
        var eligibleTargets = game.Phase == GamePhase.AwaitingRobberyTarget && isCurrentPlayer
            ? GetEligibleRobberyTargets(game, board)
                .Select(player => new RobberyTarget(player.UserId, userNames.GetValueOrDefault(player.UserId, "Unknown")))
                .ToList()
            : [];

        return new ActiveGameReadModel(
            game.Id,
            game.Name,
            game.Phase!.Value,
            userNames.GetValueOrDefault(game.CurrentPlayerUserId!, "Unknown"),
            isCurrentPlayer,
            game.PendingSettlementVertexId is null,
            game.LatestDie1,
            game.LatestDie2,
            Inventory(ownPlayer),
            ownPlayer.RequiredDiscardCount,
            game.Players.OrderBy(player => player.TurnOrder)
                .Select(player => new PublicPlayerState(
                    player.UserId,
                    userNames.GetValueOrDefault(player.UserId, "Unknown"),
                    player.Color,
                    player.TotalResources,
                    player.VisibleVictoryPoints,
                    player.UserId == game.CurrentPlayerUserId))
                .ToList(),
            eligibleTargets,
            board,
            availableConstructionVertices,
            validSettlements,
            validRoads,
            construction);
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
        player.VisibleVictoryPoints += 1;
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

    public async Task<DiceRollResult> RollDiceAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnProduction, "Dice may only be rolled once at the start of the active player's turn.");

        var die1 = RandomNumberGenerator.GetInt32(1, 7);
        var die2 = RandomNumberGenerator.GetInt32(1, 7);
        var total = die1 + die2;
        game.LatestDie1 = die1;
        game.LatestDie2 = die2;
        var production = new List<ProductionSummary>();

        if (total == 7)
        {
            foreach (var player in game.Players)
            {
                player.RequiredDiscardCount = player.TotalResources > 7 ? player.TotalResources / 2 : 0;
            }

            game.Phase = game.Players.Any(player => player.RequiredDiscardCount > 0)
                ? GamePhase.AwaitingDiscards
                : GamePhase.AwaitingRobberPlacement;
        }
        else
        {
            var produced = ProduceResources(game, DeserializeBoard(game.BoardStateJson!), total);
            production.AddRange(produced.Select(item => new ProductionSummary(item.Key, item.Value)));
            game.Phase = GamePhase.TurnActions;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DiceRollResult(die1, die2, production, game.Phase == GamePhase.AwaitingDiscards);
    }

    public async Task DiscardResourcesAsync(string userId, Guid gameId, ResourceDiscard discard, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        if (discard.Brick < 0 || discard.Lumber < 0 || discard.Wool < 0 || discard.Grain < 0 || discard.Ore < 0)
        {
            throw new ArgumentException("Discard quantities cannot be negative.", nameof(discard));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        if (game.Phase != GamePhase.AwaitingDiscards)
        {
            throw new InvalidOperationException("The game is not awaiting discards.");
        }

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        if (player.RequiredDiscardCount <= 0)
        {
            throw new InvalidOperationException("This player is not required to discard.");
        }

        if (discard.Total != player.RequiredDiscardCount)
        {
            throw new InvalidOperationException($"Exactly {player.RequiredDiscardCount} cards must be discarded.");
        }

        player.RemoveResource(ResourceType.Brick, discard.Brick);
        player.RemoveResource(ResourceType.Lumber, discard.Lumber);
        player.RemoveResource(ResourceType.Wool, discard.Wool);
        player.RemoveResource(ResourceType.Grain, discard.Grain);
        player.RemoveResource(ResourceType.Ore, discard.Ore);
        player.RequiredDiscardCount = 0;
        if (game.Players.All(candidate => candidate.RequiredDiscardCount == 0))
        {
            game.Phase = GamePhase.AwaitingRobberPlacement;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RobberMoveResult> MoveRobberAsync(string userId, Guid gameId, int hexId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.AwaitingRobberPlacement, "The robber cannot be moved now.");
        var board = DeserializeBoard(game.BoardStateJson!);
        if (board.Hexes.All(hex => hex.Id != hexId))
        {
            throw new ArgumentException("The selected terrain hex does not exist.", nameof(hexId));
        }

        if (board.RobberHexId == hexId)
        {
            throw new InvalidOperationException("The robber must move to a different terrain hex.");
        }

        board.RobberHexId = hexId;
        var requiresTarget = GetEligibleRobberyTargets(game, board).Count > 0;
        game.Phase = requiresTarget ? GamePhase.AwaitingRobberyTarget : GamePhase.TurnActions;
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RobberMoveResult(hexId, requiresTarget);
    }

    public async Task<RobberyResult> RobPlayerAsync(string userId, Guid gameId, string targetUserId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.AwaitingRobberyTarget, "A robbery target cannot be selected now.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var target = GetEligibleRobberyTargets(game, board).SingleOrDefault(player => player.UserId == targetUserId)
            ?? throw new InvalidOperationException("That player is not an eligible robbery target.");

        var stolenResource = SelectRandomResource(target);
        target.RemoveResource(stolenResource, 1);
        game.Players.Single(player => player.UserId == userId).AddResource(stolenResource, 1);
        game.Phase = GamePhase.TurnActions;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RobberyResult(target.UserId);
    }

    public async Task<TurnChangeResult> EndTurnAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "The turn cannot end before production and robber resolution are complete.");
        var players = game.Players.OrderBy(player => player.TurnOrder).ToList();
        var currentIndex = players.FindIndex(player => player.UserId == userId);
        var nextPlayer = players[(currentIndex + 1) % players.Count];
        game.CurrentPlayerUserId = nextPlayer.UserId;
        game.Phase = GamePhase.TurnProduction;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TurnChangeResult(nextPlayer.UserId);
    }

    public async Task<BuildResult> BuildRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Roads may only be built by the active player during turn actions.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var edge = board.Edges.SingleOrDefault(candidate => candidate.Id == edgeId)
            ?? throw new ArgumentException("The selected edge does not exist.", nameof(edgeId));
        if (edge.Road is not null) throw new InvalidOperationException("That edge already contains a road.");
        if (board.Edges.Count(candidate => candidate.Road?.UserId == userId) >= 15)
            throw new InvalidOperationException("The player has no road pieces remaining.");
        if (!GetValidRoadBuildEdges(board, userId).Contains(edgeId))
            throw new InvalidOperationException("The road must connect to this player's uninterrupted road or building network.");

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        EnsureResources(player, (ResourceType.Brick, 1), (ResourceType.Lumber, 1));
        edge.Road = new RoadState { UserId = userId, Color = player.Color };
        player.RemoveResource(ResourceType.Brick, 1);
        player.RemoveResource(ResourceType.Lumber, 1);
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BuildResult("Road", edgeId, userId);
    }

    public async Task<BuildResult> BuildSettlementAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Settlements may only be built by the active player during turn actions.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var vertex = board.Vertices.SingleOrDefault(candidate => candidate.Id == vertexId)
            ?? throw new ArgumentException("The selected intersection does not exist.", nameof(vertexId));
        if (vertex.Settlement is not null) throw new InvalidOperationException("That intersection already contains a building.");
        if (board.Vertices.Count(candidate => candidate.Settlement?.UserId == userId
                && candidate.Settlement.BuildingType == BuildingType.Settlement) >= 5)
            throw new InvalidOperationException("The player has no settlement pieces remaining.");
        if (!GetValidSettlementBuildVertices(board, userId).Contains(vertexId))
            throw new InvalidOperationException("The settlement must connect to this player's road and respect the distance rule.");

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        EnsureResources(player, (ResourceType.Brick, 1), (ResourceType.Lumber, 1), (ResourceType.Wool, 1), (ResourceType.Grain, 1));
        vertex.Settlement = new SettlementState { UserId = userId, Color = player.Color, BuildingType = BuildingType.Settlement };
        player.RemoveResource(ResourceType.Brick, 1);
        player.RemoveResource(ResourceType.Lumber, 1);
        player.RemoveResource(ResourceType.Wool, 1);
        player.RemoveResource(ResourceType.Grain, 1);
        player.VisibleVictoryPoints += 1;
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BuildResult("Settlement", vertexId, userId);
    }

    public async Task<BuildResult> BuildCityAsync(string userId, Guid gameId, int vertexId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Cities may only be built by the active player during turn actions.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var vertex = board.Vertices.SingleOrDefault(candidate => candidate.Id == vertexId)
            ?? throw new ArgumentException("The selected intersection does not exist.", nameof(vertexId));
        if (vertex.Settlement?.UserId != userId || vertex.Settlement.BuildingType != BuildingType.Settlement)
            throw new InvalidOperationException("A city may only replace one of the player's own settlements.");
        if (board.Vertices.Count(candidate => candidate.Settlement?.UserId == userId
                && candidate.Settlement.BuildingType == BuildingType.City) >= 4)
            throw new InvalidOperationException("The player has no city pieces remaining.");

        var player = game.Players.Single(candidate => candidate.UserId == userId);
        EnsureResources(player, (ResourceType.Ore, 3), (ResourceType.Grain, 2));
        vertex.Settlement.BuildingType = BuildingType.City;
        player.RemoveResource(ResourceType.Ore, 3);
        player.RemoveResource(ResourceType.Grain, 2);
        player.VisibleVictoryPoints += 1;
        game.BoardStateJson = SerializeBoard(board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BuildResult("City", vertexId, userId);
    }

    private async Task<Game> LoadActiveGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        await dbContext.Games.Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game was not found.");

    private static HashSet<int> GetValidRoadBuildEdges(BoardState board, string userId) =>
        board.Edges
            .Where(edge => edge.Road is null
                && (CanExtendRoadAtVertex(board, edge, edge.VertexAId, userId)
                    || CanExtendRoadAtVertex(board, edge, edge.VertexBId, userId)))
            .Select(edge => edge.Id)
            .ToHashSet();

    private static bool CanExtendRoadAtVertex(BoardState board, BoardEdge candidateEdge, int vertexId, string userId)
    {
        var vertex = board.Vertices[vertexId];
        if (vertex.Settlement?.UserId == userId) return true;
        if (vertex.Settlement is not null) return false;
        return vertex.EdgeIds
            .Where(edgeId => edgeId != candidateEdge.Id)
            .Any(edgeId => board.Edges[edgeId].Road?.UserId == userId);
    }

    private static HashSet<int> GetValidSettlementBuildVertices(BoardState board, string userId) =>
        board.Vertices
            .Where(vertex => vertex.Settlement is null
                && vertex.AdjacentVertexIds.All(adjacentId => board.Vertices[adjacentId].Settlement is null)
                && vertex.EdgeIds.Any(edgeId => board.Edges[edgeId].Road?.UserId == userId))
            .Select(vertex => vertex.Id)
            .ToHashSet();

    private static HashSet<int> GetValidCityBuildVertices(BoardState board, string userId) =>
        board.Vertices
            .Where(vertex => vertex.Settlement?.UserId == userId
                && vertex.Settlement.BuildingType == BuildingType.Settlement)
            .Select(vertex => vertex.Id)
            .ToHashSet();

    private static void EnsureResources(GamePlayer player, params (ResourceType Resource, int Amount)[] costs)
    {
        if (costs.Any(cost => player.GetResource(cost.Resource) < cost.Amount))
        {
            throw new InvalidOperationException("The player does not have the resources required for this build.");
        }
    }

    private static Dictionary<string, int> ProduceResources(Game game, BoardState board, int diceTotal)
    {
        var players = game.Players.ToDictionary(player => player.UserId);
        var produced = new Dictionary<string, int>();
        foreach (var hex in board.Hexes.Where(hex => hex.NumberToken == diceTotal && hex.Id != board.RobberHexId))
        {
            var resource = ResourceForTerrain(hex.Terrain);
            if (resource is null) continue;

            foreach (var settlement in hex.VertexIds
                .Select(vertexId => board.Vertices[vertexId].Settlement)
                .Where(settlement => settlement is not null))
            {
                var amount = settlement!.ProductionAmount;
                players[settlement.UserId].AddResource(resource.Value, amount);
                produced[settlement.UserId] = produced.GetValueOrDefault(settlement.UserId) + amount;
            }
        }

        return produced;
    }

    private static ResourceType? ResourceForTerrain(TerrainType terrain) => terrain switch
    {
        TerrainType.Hills => ResourceType.Brick,
        TerrainType.Forest => ResourceType.Lumber,
        TerrainType.Pasture => ResourceType.Wool,
        TerrainType.Fields => ResourceType.Grain,
        TerrainType.Mountains => ResourceType.Ore,
        TerrainType.Desert => null,
        _ => null
    };

    private static List<GamePlayer> GetEligibleRobberyTargets(Game game, BoardState board)
    {
        var adjacentUserIds = board.Hexes.Single(hex => hex.Id == board.RobberHexId).VertexIds
            .Select(vertexId => board.Vertices[vertexId].Settlement?.UserId)
            .Where(userId => userId is not null && userId != game.CurrentPlayerUserId)
            .ToHashSet();
        return game.Players
            .Where(player => adjacentUserIds.Contains(player.UserId) && player.TotalResources > 0)
            .ToList();
    }

    private static ResourceType SelectRandomResource(GamePlayer player)
    {
        var cardIndex = RandomNumberGenerator.GetInt32(player.TotalResources);
        foreach (var resource in Enum.GetValues<ResourceType>())
        {
            var count = player.GetResource(resource);
            if (cardIndex < count) return resource;
            cardIndex -= count;
        }

        throw new InvalidOperationException("The selected player has no resource cards.");
    }

    private static ResourceInventory Inventory(GamePlayer player) =>
        new(player.Brick, player.Lumber, player.Wool, player.Grain, player.Ore);

    private static void EnsureCurrentPlayerAndPhase(Game game, string userId, GamePhase phase, string error)
    {
        if (game.CurrentPlayerUserId != userId || game.Phase != phase)
        {
            throw new InvalidOperationException(error);
        }
    }

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
