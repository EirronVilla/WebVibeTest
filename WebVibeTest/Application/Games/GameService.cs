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
        game.PrimaryPlayerUserId = orderedPlayers[0].UserId;
        game.SecondaryPlayerUserId = null;
        game.IsSecondaryActionPhase = false;
        game.PendingSettlementVertexId = null;
        game.DevelopmentDeckJson = SerializeDeck(CreateDevelopmentDeck(game.Players.Count));
        game.ResourceBankJson = SerializeBank(CreateResourceBank(game.Players.Count));
        game.TurnNumber = 1;
        game.DevelopmentCardPlayedThisTurn = false;
        game.FreeRoadsRemaining = 0;
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
        var awardNames = new Dictionary<string, string>();
        foreach (var identity in userNames) awardNames[identity.Key] = identity.Value;
        var cards = await dbContext.DevelopmentCards.AsNoTracking()
            .Where(card => card.GameId == gameId && card.PlayedAt == null)
            .ToListAsync(cancellationToken);
        var cardCounts = cards.GroupBy(card => card.OwnerUserId).ToDictionary(group => group.Key, group => group.Count());
        var deck = string.IsNullOrWhiteSpace(game.DevelopmentDeckJson)
            ? CreateDevelopmentDeck(game.Players.Count)
            : DeserializeDeck(game.DevelopmentDeckJson);
        var bank = DeserializeBank(game.ResourceBankJson, game);
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
        var openOffers = await dbContext.TradeOffers
            .AsNoTracking()
            .Include(offer => offer.Responses)
            .Where(offer => offer.GameId == gameId && offer.Status == TradeStatus.Open
                && (offer.ProposerUserId == userId || offer.Responses.Any(response => response.UserId == userId)))
            .OrderBy(offer => offer.CreatedAt)
            .ToListAsync(cancellationToken);
        var trading = new TradingReadModel(
            openOffers.Select(offer => new TradeOfferReadModel(
                offer.Id,
                userNames.GetValueOrDefault(offer.ProposerUserId, "Unknown"),
                offer.ProposerUserId == userId,
                OfferedBundle(offer),
                RequestedBundle(offer),
                offer.Responses.SingleOrDefault(response => response.UserId == userId)?.Status,
                offer.ProposerUserId == userId
                    ? offer.Responses.Select(response => new TradeResponseReadModel(
                        response.UserId,
                        userNames.GetValueOrDefault(response.UserId, "Unknown"),
                        response.Status)).ToList()
                    : []))
                .ToList(),
            GetMaritimeRates(board, userId));
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
                    player.VisibleVictoryPoints
                        + (game.LongestRoadHolderUserId == player.UserId ? 2 : 0)
                        + (game.LargestArmyHolderUserId == player.UserId ? 2 : 0),
                    cardCounts.GetValueOrDefault(player.UserId),
                    player.UserId == game.CurrentPlayerUserId))
                .ToList(),
            eligibleTargets,
            board,
            availableConstructionVertices,
            validSettlements,
            validRoads,
            construction,
            trading,
            new AwardsReadModel(
                game.LongestRoadHolderUserId,
                game.LongestRoadHolderUserId is null ? null : awardNames.GetValueOrDefault(game.LongestRoadHolderUserId),
                game.LongestRoadLength,
                game.LargestArmyHolderUserId,
                game.LargestArmyHolderUserId is null ? null : awardNames.GetValueOrDefault(game.LargestArmyHolderUserId)),
            new PairedTurnReadModel(game.PrimaryPlayerUserId, game.SecondaryPlayerUserId, game.IsSecondaryActionPhase, game.CurrentPlayerUserId),
            new DevelopmentCardsReadModel(
                cards.Where(card => card.OwnerUserId == userId)
                    .OrderBy(card => card.PurchasedAt)
                    .Select(card => new DevelopmentCardReadModel(
                        card.Id,
                        card.Type,
                        CanPlayDevelopmentCard(game, userId, card),
                        card.PurchasedTurnNumber == game.TurnNumber))
                    .ToList(),
                deck.Count,
                new ResourceInventory(bank.Brick, bank.Lumber, bank.Wool, bank.Grain, bank.Ore),
                isCurrentPlayer && game.Phase == GamePhase.TurnActions && deck.Count > 0
                    && ownPlayer.Ore > 0 && ownPlayer.Wool > 0 && ownPlayer.Grain > 0,
                ownPlayer.VisibleVictoryPoints
                    + cards.Count(card => card.OwnerUserId == userId && card.Type == DevelopmentCardType.VictoryPoint)
                    + (game.LongestRoadHolderUserId == userId ? 2 : 0)
                    + (game.LargestArmyHolderUserId == userId ? 2 : 0),
                ownPlayer.KnightsPlayed,
                game.FreeRoadsRemaining,
                isCurrentPlayer && game.Phase == GamePhase.AwaitingRoadBuilding
                    ? GetValidRoadBuildEdges(board, userId)
                    : new HashSet<int>()));
    }

    public async Task<CompletedGameReadModel> GetCompletedGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        var game = await dbContext.Games.AsNoTracking().Include(g => g.Players)
            .SingleOrDefaultAsync(g => g.Id == gameId, cancellationToken) ?? throw new KeyNotFoundException("The game was not found.");
        if (!game.Players.Any(p => p.UserId == userId)) throw new UnauthorizedAccessException("Only participants may view this game.");
        if (game.Status != GameStatus.Completed) throw new InvalidOperationException("The game is not completed.");
        var ids = game.Players.Select(p => p.UserId).ToList();
        var names = await dbContext.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? u.Id, cancellationToken);
        return new CompletedGameReadModel(game.Id, game.Name, names.GetValueOrDefault(game.WinnerUserId!, "Unknown"),
            game.Players.OrderBy(p => p.FinalRank).Select(p => new FinalPlayerResult(names.GetValueOrDefault(p.UserId, "Unknown"), p.FinalVictoryPoints ?? 0, p.FinalRank ?? 0, p.IsWinner, p.RoadsBuilt, p.SettlementsBuilt, p.CitiesBuilt, p.DevelopmentCardsBought, p.DevelopmentCardsPlayed, p.TotalResourcesGained)).ToList(),
            names.GetValueOrDefault(game.LongestRoadHolderUserId!, "Unclaimed"), names.GetValueOrDefault(game.LargestArmyHolderUserId!, "Unclaimed"));
    }

    public async Task<CompletedGameReadModel> GetCompletedGamePublicAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var game = await dbContext.Games.AsNoTracking().Include(g => g.Players).SingleOrDefaultAsync(g => g.Id == gameId, cancellationToken)
            ?? throw new KeyNotFoundException("The game was not found.");
        if (game.Status != GameStatus.Completed) throw new InvalidOperationException("The game is not completed.");
        var ids = game.Players.Select(p => p.UserId).ToList();
        var names = await dbContext.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? u.Id, cancellationToken);
        return new CompletedGameReadModel(game.Id, game.Name, names.GetValueOrDefault(game.WinnerUserId!, "Unknown"), game.Players.OrderBy(p => p.FinalRank).Select(p => new FinalPlayerResult(names.GetValueOrDefault(p.UserId, "Unknown"), p.FinalVictoryPoints ?? 0, p.FinalRank ?? 0, p.IsWinner, p.RoadsBuilt, p.SettlementsBuilt, p.CitiesBuilt, p.DevelopmentCardsBought, p.DevelopmentCardsPlayed, p.TotalResourcesGained)).ToList(), names.GetValueOrDefault(game.LongestRoadHolderUserId!, "Unclaimed"), names.GetValueOrDefault(game.LargestArmyHolderUserId!, "Unclaimed"));
    }

    public async Task<UserStatistics> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        var results = await dbContext.GamePlayers.AsNoTracking().Where(p => p.UserId == userId && p.Game.Status == GameStatus.Completed).ToListAsync(cancellationToken);
        var played = results.Count;
        var wins = results.Count(p => p.IsWinner);
        return new UserStatistics(played, wins, played == 0 ? 0 : Math.Round(100m * wins / played, 2), results.Sum(p => p.FinalVictoryPoints ?? 0), played == 0 ? 0 : Math.Round(results.Average(p => (decimal)(p.FinalVictoryPoints ?? 0)), 2), played == 0 ? 0 : Math.Round(results.Average(p => (decimal)(p.FinalRank ?? 0)), 2));
    }

    public Task<bool> IsCompletedAsync(Guid gameId, CancellationToken cancellationToken = default) => dbContext.Games.AsNoTracking().AnyAsync(g => g.Id == gameId && g.Status == GameStatus.Completed, cancellationToken);

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
        RecalculateAwards(game, board);
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
        RecalculateAwards(game, board);
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
        if (game.Players.Count >= 5 && game.CurrentPlayerUserId != game.PrimaryPlayerUserId) throw new InvalidOperationException("The secondary player does not roll dice.");

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
            var bank = DeserializeBank(game.ResourceBankJson, game);
            var produced = ProduceResources(game, DeserializeBoard(game.BoardStateJson!), total, bank);
            game.ResourceBankJson = SerializeBank(bank);
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
        var bank = DeserializeBank(game.ResourceBankJson, game);
        foreach (var (resource, amount) in BundleValues(new ResourceBundle(discard.Brick, discard.Lumber, discard.Wool, discard.Grain, discard.Ore)))
            bank.Add(resource, amount);
        game.ResourceBankJson = SerializeBank(bank);
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
        game.PrimaryPlayerUserId ??= game.CurrentPlayerUserId;
        if (players.Count >= 5 && !game.IsSecondaryActionPhase)
        {
            var primaryIndex = players.FindIndex(player => player.UserId == userId);
            var secondary = players[(primaryIndex + 3) % players.Count];
            game.SecondaryPlayerUserId = secondary.UserId;
            game.CurrentPlayerUserId = secondary.UserId;
            game.IsSecondaryActionPhase = true;
            game.Phase = GamePhase.TurnActions;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TurnChangeResult(secondary.UserId, []);
        }
        var currentIndex = players.FindIndex(player => player.UserId == userId);
        var nextPlayer = players[(currentIndex + 1) % players.Count];
        var openOffers = await dbContext.TradeOffers
            .Where(offer => offer.GameId == gameId && offer.Status == TradeStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var offer in openOffers) offer.Status = TradeStatus.Cancelled;
        game.CurrentPlayerUserId = nextPlayer.UserId;
        game.PrimaryPlayerUserId = nextPlayer.UserId;
        game.SecondaryPlayerUserId = null;
        game.IsSecondaryActionPhase = false;
        game.Phase = GamePhase.TurnProduction;
        game.TurnNumber += 1;
        game.DevelopmentCardPlayedThisTurn = false;
        game.FreeRoadsRemaining = 0;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TurnChangeResult(nextPlayer.UserId, openOffers.Select(offer => offer.Id).ToList());
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
        ReturnToBank(game, (ResourceType.Brick, 1), (ResourceType.Lumber, 1));
        game.BoardStateJson = SerializeBoard(board);
        RecalculateAwards(game, board);
        await CompleteGameIfNeededAsync(game, cancellationToken);
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
        ReturnToBank(game, (ResourceType.Brick, 1), (ResourceType.Lumber, 1), (ResourceType.Wool, 1), (ResourceType.Grain, 1));
        player.VisibleVictoryPoints += 1;
        game.BoardStateJson = SerializeBoard(board);
        RecalculateAwards(game, board);
        await CompleteGameIfNeededAsync(game, cancellationToken);
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
        ReturnToBank(game, (ResourceType.Ore, 3), (ResourceType.Grain, 2));
        player.VisibleVictoryPoints += 1;
        game.BoardStateJson = SerializeBoard(board);
        RecalculateAwards(game, board);
        await CompleteGameIfNeededAsync(game, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BuildResult("City", vertexId, userId);
    }

    public async Task<DevelopmentCardPurchaseResult> BuyDevelopmentCardAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Development cards may only be bought during the active player's action phase.");
        var deck = string.IsNullOrWhiteSpace(game.DevelopmentDeckJson)
            ? CreateDevelopmentDeck(game.Players.Count)
            : DeserializeDeck(game.DevelopmentDeckJson);
        if (deck.Count == 0) throw new InvalidOperationException("The development-card deck is empty.");
        var player = game.Players.Single(candidate => candidate.UserId == userId);
        EnsureResources(player, (ResourceType.Ore, 1), (ResourceType.Wool, 1), (ResourceType.Grain, 1));
        var bank = DeserializeBank(game.ResourceBankJson, game);
        foreach (var resource in new[] { ResourceType.Ore, ResourceType.Wool, ResourceType.Grain })
        {
            player.RemoveResource(resource, 1);
            bank.Add(resource, 1);
        }
        var type = deck[^1];
        deck.RemoveAt(deck.Count - 1);
        var card = new DevelopmentCard
        {
            Id = Guid.NewGuid(), GameId = gameId, OwnerUserId = userId, Type = type,
            PurchasedTurnNumber = game.TurnNumber, PurchasedAt = DateTime.UtcNow
        };
        dbContext.DevelopmentCards.Add(card);
        player.DevelopmentCardsBought++;
        game.DevelopmentDeckJson = SerializeDeck(deck);
        game.ResourceBankJson = SerializeBank(bank);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CompleteGameIfNeededAsync(game, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DevelopmentCardPurchaseResult(card.Id, type, userId);
    }

    public async Task<DevelopmentCardPlayResult> PlayKnightAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        var card = await LoadPlayableCardAsync(game, userId, cardId, DevelopmentCardType.Knight, cancellationToken);
        game.Players.Single(player => player.UserId == userId).KnightsPlayed++;
        CompleteCardPlay(game, card);
        RecalculateAwards(game, DeserializeBoard(game.BoardStateJson!));
        await CompleteGameIfNeededAsync(game, cancellationToken);
        game.Phase = GamePhase.AwaitingRobberPlacement;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(card.Id, card.Type, userId);
    }

    public async Task<DevelopmentCardPlayResult> PlayRoadBuildingAsync(string userId, Guid gameId, Guid cardId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        var card = await LoadPlayableCardAsync(game, userId, cardId, DevelopmentCardType.RoadBuilding, cancellationToken);
        var board = DeserializeBoard(game.BoardStateJson!);
        var roadsAvailable = 15 - board.Edges.Count(edge => edge.Road?.UserId == userId);
        CompleteCardPlay(game, card);
        await CompleteGameIfNeededAsync(game, cancellationToken);
        game.FreeRoadsRemaining = Math.Min(2, roadsAvailable);
        game.Phase = game.FreeRoadsRemaining > 0 && GetValidRoadBuildEdges(board, userId).Count > 0
            ? GamePhase.AwaitingRoadBuilding : GamePhase.TurnActions;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(card.Id, card.Type, userId);
    }

    public async Task<DevelopmentCardPlayResult> PlayYearOfPlentyAsync(string userId, Guid gameId, Guid cardId, ResourceType first, ResourceType second, CancellationToken cancellationToken = default)
    {
        EnsureResourceType(first); EnsureResourceType(second);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        var card = await LoadPlayableCardAsync(game, userId, cardId, DevelopmentCardType.YearOfPlenty, cancellationToken);
        var bank = DeserializeBank(game.ResourceBankJson, game);
        if (bank.Get(first) < (first == second ? 2 : 1) || bank.Get(second) < 1)
            throw new InvalidOperationException("The bank does not contain the selected resources.");
        var player = game.Players.Single(candidate => candidate.UserId == userId);
        foreach (var resource in new[] { first, second }) { bank.Remove(resource, 1); player.AddResource(resource, 1); }
        CompleteCardPlay(game, card);
        await CompleteGameIfNeededAsync(game, cancellationToken);
        game.ResourceBankJson = SerializeBank(bank);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(card.Id, card.Type, userId);
    }

    public async Task<DevelopmentCardPlayResult> PlayMonopolyAsync(string userId, Guid gameId, Guid cardId, ResourceType resource, CancellationToken cancellationToken = default)
    {
        EnsureResourceType(resource);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        var card = await LoadPlayableCardAsync(game, userId, cardId, DevelopmentCardType.Monopoly, cancellationToken);
        var owner = game.Players.Single(player => player.UserId == userId);
        foreach (var opponent in game.Players.Where(player => player.UserId != userId))
        {
            var amount = opponent.GetResource(resource);
            if (amount == 0) continue;
            opponent.RemoveResource(resource, amount); owner.AddResource(resource, amount);
        }
        CompleteCardPlay(game, card);
        await CompleteGameIfNeededAsync(game, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(card.Id, card.Type, userId);
    }

    public async Task<BuildResult> BuildFreeRoadAsync(string userId, Guid gameId, int edgeId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.AwaitingRoadBuilding, "A free road cannot be placed now.");
        if (game.FreeRoadsRemaining <= 0) throw new InvalidOperationException("No free road placements remain.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var edge = board.Edges.SingleOrDefault(candidate => candidate.Id == edgeId) ?? throw new ArgumentException("The edge does not exist.");
        if (!GetValidRoadBuildEdges(board, userId).Contains(edgeId)) throw new InvalidOperationException("That is not a valid road location.");
        var player = game.Players.Single(candidate => candidate.UserId == userId);
        if (board.Edges.Count(candidate => candidate.Road?.UserId == userId) >= 15) throw new InvalidOperationException("No road pieces remain.");
        edge.Road = new RoadState { UserId = userId, Color = player.Color };
        game.FreeRoadsRemaining--;
        if (game.FreeRoadsRemaining == 0 || GetValidRoadBuildEdges(board, userId).Count == 0) game.Phase = GamePhase.TurnActions;
        game.BoardStateJson = SerializeBoard(board);
        RecalculateAwards(game, board);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new("Road", edgeId, userId);
    }

    public async Task FinishRoadBuildingAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.AwaitingRoadBuilding, "Road Building is not being resolved.");
        game.FreeRoadsRemaining = 0; game.Phase = GamePhase.TurnActions;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> CanAccessGameAsync(string userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.FromResult(false);
        return dbContext.GamePlayers.AsNoTracking()
            .AnyAsync(player => player.GameId == gameId && player.UserId == userId, cancellationToken);
    }

    public async Task<TradeEventResult> ProposeTradeAsync(
        string userId, Guid gameId, ResourceBundle offered, ResourceBundle requested, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        ValidateTradeBundles(offered, requested);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Trades may only be proposed by the active player during turn actions.");
        if (game.Players.Count >= 5 && game.IsSecondaryActionPhase) throw new InvalidOperationException("The secondary action player may not propose player-to-player trades.");
        var proposer = game.Players.Single(player => player.UserId == userId);
        EnsureBundleOwned(proposer, offered);
        var opponents = game.Players.Where(player => player.UserId != userId).ToList();
        if (opponents.Count == 0) throw new InvalidOperationException("There are no eligible trade partners.");

        var offer = CreateTradeOffer(gameId, userId, offered, requested);
        foreach (var opponent in opponents)
        {
            offer.Responses.Add(new TradeResponse
            {
                Id = Guid.NewGuid(),
                UserId = opponent.UserId,
                Status = TradeResponseStatus.Pending
            });
        }

        dbContext.TradeOffers.Add(offer);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TradeEventResult(offer.Id, game.Players.Select(player => player.UserId).ToList());
    }

    public async Task<TradeEventResult> RespondToTradeAsync(
        string userId, Guid gameId, Guid offerId, bool accept, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        if (game.Phase != GamePhase.TurnActions || game.CurrentPlayerUserId is null)
            throw new InvalidOperationException("Trades cannot be answered outside the active player's action phase.");
        if (game.Players.Count >= 5 && game.IsSecondaryActionPhase) throw new InvalidOperationException("Player-to-player trades are unavailable during the paired action phase.");
        var offer = await LoadOpenTradeOfferAsync(gameId, offerId, cancellationToken);
        if (offer.ProposerUserId == userId) throw new InvalidOperationException("A player cannot respond to their own trade.");
        if (offer.ProposerUserId != game.CurrentPlayerUserId)
            throw new InvalidOperationException("The trade was not proposed by the active player.");
        var response = offer.Responses.SingleOrDefault(candidate => candidate.UserId == userId)
            ?? throw new UnauthorizedAccessException("This player is not a participant in the trade.");
        if (accept) EnsureBundleOwned(game.Players.Single(player => player.UserId == userId), RequestedBundle(offer));
        response.Status = accept ? TradeResponseStatus.Accepted : TradeResponseStatus.Rejected;
        response.RespondedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TradeEventResult(offer.Id, game.Players.Select(player => player.UserId).ToList());
    }

    public async Task<TradeEventResult> FinalizeTradeAsync(
        string userId, Guid gameId, Guid offerId, string acceptingUserId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        if (userId == acceptingUserId) throw new InvalidOperationException("A player cannot trade with themselves.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Only the active player may finalize a trade during turn actions.");
        var offer = await LoadOpenTradeOfferAsync(gameId, offerId, cancellationToken);
        if (offer.ProposerUserId != userId) throw new UnauthorizedAccessException("Only the proposer may finalize this trade.");
        var acceptedResponse = offer.Responses.SingleOrDefault(response => response.UserId == acceptingUserId
            && response.Status == TradeResponseStatus.Accepted)
            ?? throw new InvalidOperationException("That player has not accepted this trade.");
        var proposer = game.Players.Single(player => player.UserId == userId);
        var acceptingPlayer = game.Players.Single(player => player.UserId == acceptingUserId);
        var offered = OfferedBundle(offer);
        var requested = RequestedBundle(offer);
        EnsureBundleOwned(proposer, offered);
        EnsureBundleOwned(acceptingPlayer, requested);
        TransferBundle(proposer, acceptingPlayer, offered);
        TransferBundle(acceptingPlayer, proposer, requested);
        offer.Status = TradeStatus.Completed;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TradeEventResult(offer.Id, game.Players.Select(player => player.UserId).ToList());
    }

    public async Task<TradeEventResult> CancelTradeAsync(
        string userId, Guid gameId, Guid offerId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Only the active player may cancel a trade during turn actions.");
        var offer = await LoadOpenTradeOfferAsync(gameId, offerId, cancellationToken);
        if (offer.ProposerUserId != userId) throw new UnauthorizedAccessException("Only the proposer may cancel this trade.");
        offer.Status = TradeStatus.Cancelled;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TradeEventResult(offer.Id, game.Players.Select(player => player.UserId).ToList());
    }

    public async Task<MaritimeTradeResult> MaritimeTradeAsync(
        string userId, Guid gameId, ResourceType give, ResourceType receive, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated(userId);
        if (!Enum.IsDefined(give) || !Enum.IsDefined(receive)) throw new ArgumentException("The selected resource type is invalid.");
        if (give == receive) throw new InvalidOperationException("The bank resource received must differ from the resource given.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var game = await LoadActiveGameAsync(gameId, cancellationToken);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Maritime trades are only allowed during the active player's turn actions.");
        var board = DeserializeBoard(game.BoardStateJson!);
        var rate = GetMaritimeRates(board, userId)[give];
        var player = game.Players.Single(candidate => candidate.UserId == userId);
        if (player.GetResource(give) < rate) throw new InvalidOperationException($"This trade requires {rate} {give} cards.");
        var bank = DeserializeBank(game.ResourceBankJson, game);
        if (bank.Get(receive) < 1) throw new InvalidOperationException("The bank does not contain the requested resource.");
        player.RemoveResource(give, rate);
        player.AddResource(receive, 1);
        bank.Add(give, rate);
        bank.Remove(receive, 1);
        game.ResourceBankJson = SerializeBank(bank);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MaritimeTradeResult(give, rate, receive);
    }

    private async Task<Game> LoadActiveGameAsync(Guid gameId, CancellationToken cancellationToken) =>
        await dbContext.Games.Include(game => game.Players)
            .SingleOrDefaultAsync(game => game.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game was not found.");

    private async Task<TradeOffer> LoadOpenTradeOfferAsync(Guid gameId, Guid offerId, CancellationToken cancellationToken) =>
        await dbContext.TradeOffers.Include(offer => offer.Responses)
            .SingleOrDefaultAsync(offer => offer.Id == offerId && offer.GameId == gameId && offer.Status == TradeStatus.Open, cancellationToken)
            ?? throw new InvalidOperationException("The trade offer is no longer open.");

    private static TradeOffer CreateTradeOffer(Guid gameId, string userId, ResourceBundle offered, ResourceBundle requested) =>
        new()
        {
            Id = Guid.NewGuid(), GameId = gameId, ProposerUserId = userId, Status = TradeStatus.Open, CreatedAt = DateTime.UtcNow,
            OfferedBrick = offered.Brick, OfferedLumber = offered.Lumber, OfferedWool = offered.Wool, OfferedGrain = offered.Grain, OfferedOre = offered.Ore,
            RequestedBrick = requested.Brick, RequestedLumber = requested.Lumber, RequestedWool = requested.Wool, RequestedGrain = requested.Grain, RequestedOre = requested.Ore
        };

    private static ResourceBundle OfferedBundle(TradeOffer offer) =>
        new(offer.OfferedBrick, offer.OfferedLumber, offer.OfferedWool, offer.OfferedGrain, offer.OfferedOre);

    private static ResourceBundle RequestedBundle(TradeOffer offer) =>
        new(offer.RequestedBrick, offer.RequestedLumber, offer.RequestedWool, offer.RequestedGrain, offer.RequestedOre);

    private static void ValidateTradeBundles(ResourceBundle offered, ResourceBundle requested)
    {
        if (offered.HasNegative || requested.HasNegative) throw new ArgumentException("Trade quantities cannot be negative.");
        if (offered.Total <= 0 || requested.Total <= 0) throw new InvalidOperationException("A trade must both offer and request at least one resource.");
    }

    private static void EnsureBundleOwned(GamePlayer player, ResourceBundle bundle)
    {
        if (player.Brick < bundle.Brick || player.Lumber < bundle.Lumber || player.Wool < bundle.Wool
            || player.Grain < bundle.Grain || player.Ore < bundle.Ore)
            throw new InvalidOperationException("A trade participant no longer owns the required resources.");
    }

    private static void TransferBundle(GamePlayer from, GamePlayer to, ResourceBundle bundle)
    {
        foreach (var (resource, amount) in BundleValues(bundle).Where(item => item.Amount > 0))
        {
            from.RemoveResource(resource, amount);
            to.AddResource(resource, amount);
        }
    }

    private static IEnumerable<(ResourceType Resource, int Amount)> BundleValues(ResourceBundle bundle)
    {
        yield return (ResourceType.Brick, bundle.Brick);
        yield return (ResourceType.Lumber, bundle.Lumber);
        yield return (ResourceType.Wool, bundle.Wool);
        yield return (ResourceType.Grain, bundle.Grain);
        yield return (ResourceType.Ore, bundle.Ore);
    }

    private static IReadOnlyDictionary<ResourceType, int> GetMaritimeRates(BoardState board, string userId)
    {
        var rates = Enum.GetValues<ResourceType>().ToDictionary(resource => resource, _ => 4);
        foreach (var port in board.Ports)
        {
            var vertexIds = port.VertexIds.Length > 0
                ? port.VertexIds
                : [board.Edges[port.EdgeId].VertexAId, board.Edges[port.EdgeId].VertexBId];
            if (!vertexIds.Any(vertexId => board.Vertices[vertexId].Settlement?.UserId == userId)) continue;
            if (port.Type == PortType.Generic)
            {
                foreach (var resource in rates.Keys.ToList()) rates[resource] = Math.Min(rates[resource], 3);
            }
            else if (ResourceForPort(port.Type) is ResourceType resource)
            {
                rates[resource] = 2;
            }
        }

        return rates;
    }

    private static ResourceType? ResourceForPort(PortType port) => port switch
    {
        PortType.Brick => ResourceType.Brick,
        PortType.Lumber => ResourceType.Lumber,
        PortType.Wool => ResourceType.Wool,
        PortType.Grain => ResourceType.Grain,
        PortType.Ore => ResourceType.Ore,
        _ => null
    };

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

    private static void RecalculateAwards(Game game, BoardState board)
    {
        var lengths = game.Players.ToDictionary(player => player.UserId, player => LongestRoad(board, player.UserId));
        var best = lengths.Values.DefaultIfEmpty(0).Max();
        if (game.LongestRoadHolderUserId is not null && lengths.GetValueOrDefault(game.LongestRoadHolderUserId) == best && best >= 5)
            game.LongestRoadLength = best;
        else
        {
            game.LongestRoadHolderUserId = best >= 5 ? lengths.First(pair => pair.Value == best).Key : null;
            game.LongestRoadLength = best;
        }

        var armyBest = game.Players.Select(player => player.KnightsPlayed).DefaultIfEmpty(0).Max();
        if (game.LargestArmyHolderUserId is not null && game.Players.Any(player => player.UserId == game.LargestArmyHolderUserId && player.KnightsPlayed == armyBest && armyBest >= 3))
            return;
        game.LargestArmyHolderUserId = armyBest >= 3
            ? game.Players.First(player => player.KnightsPlayed == armyBest).UserId
            : null;
    }

    private static int LongestRoad(BoardState board, string userId)
    {
        var owned = board.Edges.Where(edge => edge.Road?.UserId == userId).ToList();
        var best = 0;
        foreach (var edge in owned)
        {
            best = Math.Max(best, RoadPath(board, edge.Id, edge.VertexAId, userId, new HashSet<int>()));
            best = Math.Max(best, RoadPath(board, edge.Id, edge.VertexBId, userId, new HashSet<int>()));
        }
        return best;
    }

    private static int RoadPath(BoardState board, int edgeId, int vertexId, string userId, HashSet<int> used)
    {
        used.Add(edgeId);
        var vertex = board.Vertices[vertexId];
        var best = 1;
        if (vertex.Settlement is not null && vertex.Settlement.UserId != userId) return best;
        foreach (var nextId in vertex.EdgeIds)
        {
            if (used.Contains(nextId)) continue;
            var next = board.Edges[nextId];
            if (next.Road?.UserId != userId) continue;
            var nextVertex = next.VertexAId == vertexId ? next.VertexBId : next.VertexAId;
            var branchUsed = new HashSet<int>(used);
            best = Math.Max(best, 1 + RoadPath(board, nextId, nextVertex, userId, branchUsed));
        }
        return best;
    }

    private static void EnsureResources(GamePlayer player, params (ResourceType Resource, int Amount)[] costs)
    {
        if (costs.Any(cost => player.GetResource(cost.Resource) < cost.Amount))
        {
            throw new InvalidOperationException("The player does not have the resources required for this build.");
        }
    }

    private static void ReturnToBank(Game game, params (ResourceType Resource, int Amount)[] resources)
    {
        var bank = DeserializeBank(game.ResourceBankJson, game);
        foreach (var resource in resources) bank.Add(resource.Resource, resource.Amount);
        game.ResourceBankJson = SerializeBank(bank);
    }

    private async Task<DevelopmentCard> LoadPlayableCardAsync(Game game, string userId, Guid cardId, DevelopmentCardType type, CancellationToken cancellationToken)
    {
        EnsureAuthenticated(userId);
        EnsureActiveGame(game, userId);
        EnsureCurrentPlayerAndPhase(game, userId, GamePhase.TurnActions, "Development cards may only be played by the active player during turn actions.");
        if (game.DevelopmentCardPlayedThisTurn) throw new InvalidOperationException("Only one development card may be played per turn.");
        var card = await dbContext.DevelopmentCards.SingleOrDefaultAsync(candidate => candidate.Id == cardId && candidate.GameId == game.Id, cancellationToken)
            ?? throw new InvalidOperationException("The development card was not found.");
        if (card.OwnerUserId != userId || card.Type != type || card.PlayedAt is not null)
            throw new InvalidOperationException("That development card cannot be played.");
        if (card.PurchasedTurnNumber >= game.TurnNumber)
            throw new InvalidOperationException("A development card cannot be played on the turn it was purchased.");
        return card;
    }

    private static bool CanPlayDevelopmentCard(Game game, string userId, DevelopmentCard card) =>
        game.CurrentPlayerUserId == userId && game.Phase == GamePhase.TurnActions
        && !game.DevelopmentCardPlayedThisTurn && card.Type != DevelopmentCardType.VictoryPoint
        && card.PlayedAt is null && card.PurchasedTurnNumber < game.TurnNumber;

    private static void CompleteCardPlay(Game game, DevelopmentCard card)
    {
        card.PlayedAt = DateTime.UtcNow;
        game.DevelopmentCardPlayedThisTurn = true;
        game.Players.Single(player => player.UserId == card.OwnerUserId).DevelopmentCardsPlayed++;
    }

    private async Task CompleteGameIfNeededAsync(Game game, CancellationToken cancellationToken)
    {
        if (game.Status != GameStatus.InProgress) return;
        var board = DeserializeBoard(game.BoardStateJson!);
        var cards = await dbContext.DevelopmentCards.AsNoTracking().Where(c => c.GameId == game.Id && c.PlayedAt == null).ToListAsync(cancellationToken);
        var scores = game.Players.ToDictionary(p => p.UserId, p => p.VisibleVictoryPoints
            + cards.Count(c => c.OwnerUserId == p.UserId && c.Type == DevelopmentCardType.VictoryPoint)
            + (game.LongestRoadHolderUserId == p.UserId ? 2 : 0) + (game.LargestArmyHolderUserId == p.UserId ? 2 : 0));
        var winner = scores.OrderByDescending(x => x.Value).ThenBy(x => game.Players.Single(p => p.UserId == x.Key).TurnOrder).First();
        if (winner.Value < 10) return;
        game.Status = GameStatus.Completed;
        game.Phase = GamePhase.Completed;
        game.FinishedAt = DateTime.UtcNow;
        game.WinnerUserId = winner.Key;
        var ordered = scores.OrderByDescending(x => x.Value).ThenBy(x => game.Players.Single(p => p.UserId == x.Key).TurnOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var player = game.Players.Single(p => p.UserId == ordered[i].Key);
            player.FinalVictoryPoints = ordered[i].Value;
            player.FinalRank = i + 1;
            player.IsWinner = player.UserId == winner.Key;
            player.RoadsBuilt = board.Edges.Count(e => e.Road?.UserId == player.UserId);
            player.SettlementsBuilt = board.Vertices.Count(v => v.Settlement?.UserId == player.UserId && v.Settlement.BuildingType == BuildingType.Settlement);
            player.CitiesBuilt = board.Vertices.Count(v => v.Settlement?.UserId == player.UserId && v.Settlement.BuildingType == BuildingType.City);
            player.TotalResourcesGained = player.TotalResourcesGained;
        }
    }

    private static List<DevelopmentCardType> CreateDevelopmentDeck(int playerCount)
    {
        var deck = new List<DevelopmentCardType>();
        void Add(DevelopmentCardType type, int count) { for (var index = 0; index < count; index++) deck.Add(type); }
        var extension = playerCount >= 5;
        Add(DevelopmentCardType.Knight, extension ? 20 : 14);
        Add(DevelopmentCardType.RoadBuilding, extension ? 3 : 2);
        Add(DevelopmentCardType.YearOfPlenty, extension ? 3 : 2);
        Add(DevelopmentCardType.Monopoly, extension ? 3 : 2);
        Add(DevelopmentCardType.VictoryPoint, 5);
        for (var index = deck.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[swap]) = (deck[swap], deck[index]);
        }
        return deck;
    }

    private static ResourceBank CreateResourceBank(int playerCount)
    {
        var count = playerCount >= 5 ? 24 : 19;
        return new ResourceBank { Brick = count, Lumber = count, Wool = count, Grain = count, Ore = count };
    }

    private static ResourceBank DeserializeBank(string? json, Game game)
    {
        if (!string.IsNullOrWhiteSpace(json)) return JsonSerializer.Deserialize<ResourceBank>(json)!;
        var bank = CreateResourceBank(game.Players.Count);
        foreach (var player in game.Players)
            foreach (var resource in Enum.GetValues<ResourceType>())
                bank.Remove(resource, Math.Min(bank.Get(resource), player.GetResource(resource)));
        return bank;
    }

    private static string SerializeBank(ResourceBank bank) => JsonSerializer.Serialize(bank);
    private static List<DevelopmentCardType> DeserializeDeck(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<DevelopmentCardType>>(json)!;
    private static string SerializeDeck(List<DevelopmentCardType> deck) => JsonSerializer.Serialize(deck);
    private static void EnsureResourceType(ResourceType resource)
    {
        if (!Enum.IsDefined(resource)) throw new ArgumentException("The selected resource type is invalid.");
    }

    private static Dictionary<string, int> ProduceResources(Game game, BoardState board, int diceTotal, ResourceBank bank)
    {
        var players = game.Players.ToDictionary(player => player.UserId);
        var produced = new Dictionary<string, int>();
        var claims = new List<(string UserId, ResourceType Resource, int Amount)>();
        foreach (var hex in board.Hexes.Where(hex => hex.NumberToken == diceTotal && hex.Id != board.RobberHexId))
        {
            var resource = ResourceForTerrain(hex.Terrain);
            if (resource is null) continue;

            foreach (var settlement in hex.VertexIds
                .Select(vertexId => board.Vertices[vertexId].Settlement)
                .Where(settlement => settlement is not null))
            {
                var amount = settlement!.ProductionAmount;
                claims.Add((settlement.UserId, resource.Value, amount));
            }
        }

        foreach (var resourceClaims in claims.GroupBy(claim => claim.Resource))
        {
            var total = resourceClaims.Sum(claim => claim.Amount);
            if (bank.Get(resourceClaims.Key) < total) continue;
            bank.Remove(resourceClaims.Key, total);
            foreach (var claim in resourceClaims)
            {
                players[claim.UserId].AddResource(claim.Resource, claim.Amount);
                produced[claim.UserId] = produced.GetValueOrDefault(claim.UserId) + claim.Amount;
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
