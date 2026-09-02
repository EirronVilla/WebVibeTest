using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebVibeTest.Application.Games;
using WebVibeTest.Domain.Games;
using WebVibeTest.Hubs;
using WebVibeTest.Infrastructure.Data;

namespace WebVibeTest.Infrastructure.Games;

public sealed class GameTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<GameHub> hubContext,
    ILogger<GameTimeoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CancelExpiredGamesAsync(stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Failed to cancel games older than 24 hours."); }
            try { await ResolveExpiredTradesAsync(stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Failed to resolve expired trade responses."); }
            try { await ResolveExpiredTurnsAsync(stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Failed to resolve expired player turns."); }
        }
    }

    private async Task CancelExpiredGamesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);
        var games = await db.Games.Where(game => game.Status == GameStatus.InProgress && game.StartedAt <= cutoff)
            .ToListAsync(cancellationToken);
        foreach (var game in games)
        {
            game.Status = GameStatus.Cancelled;
            game.FinishedAt = now;
            game.ActionDeadlineUtc = null;
        }
        if (games.Count == 0) return;
        var gameIds = games.Select(game => game.Id).ToList();
        var offers = await db.TradeOffers.Where(offer => gameIds.Contains(offer.GameId) && offer.Status == TradeStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var offer in offers) offer.Status = TradeStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        foreach (var game in games)
            await hubContext.Clients.Group(GameHub.GroupName(game.Id)).SendAsync(GameHub.GameCancelledEvent, game.Id, cancellationToken);
    }

    private async Task ResolveExpiredTradesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var offers = await db.TradeOffers.Include(offer => offer.Responses)
            .Where(offer => offer.Game.Status == GameStatus.InProgress && offer.Status == TradeStatus.Open && offer.ResponseDeadlineUtc <= now
                && offer.Responses.Any(response => response.Status == TradeResponseStatus.Pending))
            .ToListAsync(cancellationToken);
        foreach (var offer in offers)
        {
            var players = await db.GamePlayers.Where(player => player.GameId == offer.GameId)
                .ToDictionaryAsync(player => player.UserId, cancellationToken);
            foreach (var response in offer.Responses.Where(response => response.Status == TradeResponseStatus.Pending))
            {
                response.Status = players.TryGetValue(response.UserId, out var player) && CanAfford(player, offer)
                    ? TradeResponseStatus.Accepted
                    : TradeResponseStatus.Rejected;
                response.RespondedAt = now;
            }
            var game = await db.Games.SingleAsync(game => game.Id == offer.GameId, cancellationToken);
            game.ActionDeadlineUtc = now.AddMinutes(1);
            await db.SaveChangesAsync(cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(offer.GameId))
                .SendAsync(GameHub.GameStateUpdatedEvent, offer.GameId, cancellationToken);
        }
    }

    private async Task ResolveExpiredTurnsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var uninitialized = await db.Games.Where(game => game.Status == GameStatus.InProgress && game.ActionDeadlineUtc == null).ToListAsync(cancellationToken);
        foreach (var game in uninitialized) game.ActionDeadlineUtc = now.AddMinutes(1);
        if (uninitialized.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var expired = await db.Games.AsNoTracking()
            .Where(game => game.Status == GameStatus.InProgress && game.ActionDeadlineUtc <= now
                && (game.Phase == GamePhase.TurnProduction || game.Phase == GamePhase.TurnActions))
            .Select(game => new { game.Id, game.CurrentPlayerUserId, game.Phase, game.ActionDeadlineUtc })
            .ToListAsync(cancellationToken);
        foreach (var item in expired)
        {
            if (item.CurrentPlayerUserId is null) continue;
            var service = scope.ServiceProvider.GetRequiredService<IGameService>();
            if (item.Phase == GamePhase.TurnProduction)
            {
                var result = await service.RollDiceAsync(item.CurrentPlayerUserId, item.Id, cancellationToken);
                if (result.Die1 + result.Die2 == 7) await ApplyRobberPenaltyAsync(db, item.Id, item.CurrentPlayerUserId, cancellationToken);
            }
            await service.EndTurnAsync(item.CurrentPlayerUserId, item.Id, cancellationToken);
            await hubContext.Clients.Group(GameHub.GroupName(item.Id))
                .SendAsync(GameHub.GameStateUpdatedEvent, item.Id, cancellationToken);
        }
    }

    private static async Task ApplyRobberPenaltyAsync(ApplicationDbContext db, Guid gameId, string playerUserId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var game = await db.Games.Include(game => game.Players).SingleAsync(game => game.Id == gameId, cancellationToken);
        var board = JsonSerializer.Deserialize<Domain.Board.BoardState>(game.BoardStateJson!)!;
        var target = board.Hexes.Where(hex => hex.Id != board.RobberHexId)
            .OrderByDescending(hex => hex.VertexIds.Count(vertexId => board.Vertices[vertexId].Settlement?.UserId == playerUserId))
            .First();
        board.RobberHexId = target.Id;
        foreach (var player in game.Players) player.RequiredDiscardCount = 0;
        game.BoardStateJson = JsonSerializer.Serialize(board);
        game.Phase = GamePhase.TurnActions;
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private static bool CanAfford(GamePlayer player, TradeOffer offer) =>
        player.Brick >= offer.RequestedBrick && player.Lumber >= offer.RequestedLumber
        && player.Wool >= offer.RequestedWool && player.Grain >= offer.RequestedGrain && player.Ore >= offer.RequestedOre;
}
