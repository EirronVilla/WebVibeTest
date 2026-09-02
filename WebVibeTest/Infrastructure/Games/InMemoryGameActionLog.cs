using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebVibeTest.Application.Games;
using WebVibeTest.Domain.Games;
using WebVibeTest.Hubs;
using WebVibeTest.Infrastructure.Data;

namespace WebVibeTest.Infrastructure.Games;

public sealed class InMemoryGameActionLog(
    IServiceScopeFactory scopeFactory,
    IHubContext<GameHub> hubContext,
    ILogger<InMemoryGameActionLog> logger) : IGameActionLog
{
    private const int MaximumEntries = 150;
    private readonly ConcurrentDictionary<Guid, LogBuffer> _logs = new();
    private readonly ConcurrentDictionary<Guid, AwardSnapshot> _awards = new();
    private readonly ConcurrentDictionary<Guid, byte> _completed = new();
    private long _sequence;

    public IReadOnlyList<GameActionLogEntry> GetEntries(Guid gameId)
    {
        if (!_logs.TryGetValue(gameId, out var buffer)) return [];
        lock (buffer.Gate) return buffer.Entries.ToList();
    }

    public async Task RecordAsync(GameActionEvent action, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == action.GameId, cancellationToken);
            if (game is null) return;

            var userIds = new[] { action.ActorUserId, action.TargetUserId }.Where(id => id is not null).Cast<string>().Distinct().ToList();
            var names = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.UserName ?? "Player", cancellationToken);
            var actor = DisplayName(names.GetValueOrDefault(action.ActorUserId, "Player"));
            var target = action.TargetUserId is null ? null : DisplayName(names.GetValueOrDefault(action.TargetUserId, "Player"));
            var message = await FormatAsync(db, action, actor, target, cancellationToken);
            await AppendAsync(action.GameId, game.TurnNumber, message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not append game action log entry for {GameId}.", action.GameId);
        }
    }

    public async Task CaptureAwardsAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await db.Games.AsNoTracking().Where(game => game.Id == gameId)
            .Select(game => new AwardSnapshot(game.LongestRoadHolderUserId, game.LongestRoadLength, game.LargestArmyHolderUserId))
            .SingleOrDefaultAsync(cancellationToken);
        if (state is not null) _awards[gameId] = state;
    }

    public async Task RecordAwardChangesAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == gameId, cancellationToken);
            if (game is null) return;
            var current = new AwardSnapshot(game.LongestRoadHolderUserId, game.LongestRoadLength, game.LargestArmyHolderUserId);
            var previous = _awards.GetOrAdd(gameId, new AwardSnapshot(null, 0, null));
            _awards[gameId] = current;

            if (previous.LongestRoadHolderUserId != current.LongestRoadHolderUserId)
            {
                var text = current.LongestRoadHolderUserId is null
                    ? "Longest Road is now unclaimed."
                    : $"{await GetDisplayNameAsync(db, current.LongestRoadHolderUserId, cancellationToken)} claimed Longest Road with {current.LongestRoadLength} roads.";
                await AppendAsync(gameId, game.TurnNumber, text, cancellationToken);
            }

            if (previous.LargestArmyHolderUserId != current.LargestArmyHolderUserId)
            {
                var text = current.LargestArmyHolderUserId is null
                    ? "Largest Army is now unclaimed."
                    : $"{await GetDisplayNameAsync(db, current.LargestArmyHolderUserId, cancellationToken)} claimed Largest Army.";
                await AppendAsync(gameId, game.TurnNumber, text, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not update award log entries for {GameId}.", gameId);
        }
    }

    public async Task RecordCompletionAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        if (_completed.ContainsKey(gameId)) return;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(item => item.Id == gameId, cancellationToken);
        if (game?.Status != GameStatus.Completed || game.WinnerUserId is null || !_completed.TryAdd(gameId, 0)) return;
        var winner = await GetDisplayNameAsync(db, game.WinnerUserId, cancellationToken);
        await AppendAsync(gameId, game.TurnNumber, $"{winner} won the game.", cancellationToken);
    }

    private async Task<string> FormatAsync(ApplicationDbContext db, GameActionEvent action, string actor, string? target, CancellationToken cancellationToken) =>
        action.Kind switch
        {
            GameActionKind.RoadBuilt => $"{actor} built a road.",
            GameActionKind.SettlementBuilt => $"{actor} built a settlement.",
            GameActionKind.CityBuilt => $"{actor} built a city.",
            GameActionKind.DiceRolled => $"{actor} rolled {action.DiceTotal}.",
            GameActionKind.CardsDiscarded => $"{actor} discarded {action.Quantity} cards.",
            GameActionKind.RobberMoved => $"{actor} moved the robber.",
            GameActionKind.PlayerRobbed => $"{actor} stole one card from {target ?? "another player"}.",
            GameActionKind.PlayerTradeCompleted => await FormatPlayerTradeAsync(db, action, actor, target, cancellationToken),
            GameActionKind.MaritimeTradeCompleted => $"{actor} made a maritime trade: gave {action.TradeRate} {action.GivenResource}, received 1 {action.ReceivedResource}.",
            GameActionKind.DevelopmentCardBought => $"{actor} bought a development card.",
            GameActionKind.DevelopmentCardPlayed => $"{actor} played {CardName(action.DevelopmentCardType)}.",
            _ => throw new ArgumentOutOfRangeException(nameof(action.Kind))
        };

    private static async Task<string> FormatPlayerTradeAsync(ApplicationDbContext db, GameActionEvent action, string actor, string? target, CancellationToken cancellationToken)
    {
        var offer = action.TradeOfferId is null ? null : await db.TradeOffers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == action.TradeOfferId, cancellationToken);
        if (offer is null) return $"{actor} traded with {target ?? "another player"}.";
        var offered = BundleText(offer.OfferedBrick, offer.OfferedLumber, offer.OfferedWool, offer.OfferedGrain, offer.OfferedOre);
        var requested = BundleText(offer.RequestedBrick, offer.RequestedLumber, offer.RequestedWool, offer.RequestedGrain, offer.RequestedOre);
        return $"{actor} traded with {target ?? "another player"}. Gave {offered}, received {requested}.";
    }

    private async Task AppendAsync(Guid gameId, int round, string text, CancellationToken cancellationToken)
    {
        var entry = new GameActionLogEntry(Interlocked.Increment(ref _sequence), round, $"Round {round}: {text}", DateTime.UtcNow);
        var buffer = _logs.GetOrAdd(gameId, _ => new LogBuffer());
        lock (buffer.Gate)
        {
            buffer.Entries.Add(entry);
            if (buffer.Entries.Count > MaximumEntries) buffer.Entries.RemoveRange(0, buffer.Entries.Count - MaximumEntries);
        }
        await hubContext.Clients.Group(GameHub.GroupName(gameId)).SendAsync(GameHub.ActionLogEntryAddedEvent, entry, cancellationToken);
    }

    private static string BundleText(int brick, int lumber, int wool, int grain, int ore)
    {
        var parts = new List<string>();
        if (brick > 0) parts.Add($"{brick} Brick");
        if (lumber > 0) parts.Add($"{lumber} Lumber");
        if (wool > 0) parts.Add($"{wool} Wool");
        if (grain > 0) parts.Add($"{grain} Grain");
        if (ore > 0) parts.Add($"{ore} Ore");
        return string.Join(", ", parts);
    }

    private static string CardName(DevelopmentCardType? type) => type switch
    {
        DevelopmentCardType.RoadBuilding => "Road Building",
        DevelopmentCardType.YearOfPlenty => "Year of Plenty",
        null => "a development card",
        _ => type.Value.ToString()
    };

    private static async Task<string> GetDisplayNameAsync(ApplicationDbContext db, string userId, CancellationToken cancellationToken) =>
        DisplayName(await db.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken) ?? "Player");

    private static string DisplayName(string value) => value.Split('@', 2)[0];

    private sealed class LogBuffer { public object Gate { get; } = new(); public List<GameActionLogEntry> Entries { get; } = []; }
    private sealed record AwardSnapshot(string? LongestRoadHolderUserId, int LongestRoadLength, string? LargestArmyHolderUserId);
}
