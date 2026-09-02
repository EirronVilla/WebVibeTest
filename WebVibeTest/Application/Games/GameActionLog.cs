using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public sealed record GameActionLogEntry(long Sequence, int Round, string Message, DateTime CreatedAt);

public enum GameActionKind
{
    RoadBuilt,
    SettlementBuilt,
    CityBuilt,
    DiceRolled,
    CardsDiscarded,
    RobberMoved,
    PlayerRobbed,
    PlayerTradeCompleted,
    MaritimeTradeCompleted,
    DevelopmentCardBought,
    DevelopmentCardPlayed
}

public sealed record GameActionEvent(
    Guid GameId,
    GameActionKind Kind,
    string ActorUserId,
    string? TargetUserId = null,
    int Quantity = 0,
    int DiceTotal = 0,
    Guid? TradeOfferId = null,
    ResourceType? GivenResource = null,
    ResourceType? ReceivedResource = null,
    int TradeRate = 0,
    DevelopmentCardType? DevelopmentCardType = null);

public interface IGameActionLog
{
    IReadOnlyList<GameActionLogEntry> GetEntries(Guid gameId);
    Task RecordAsync(GameActionEvent action, CancellationToken cancellationToken = default);
    Task CaptureAwardsAsync(Guid gameId, CancellationToken cancellationToken = default);
    Task RecordAwardChangesAsync(Guid gameId, CancellationToken cancellationToken = default);
    Task RecordCompletionAsync(Guid gameId, CancellationToken cancellationToken = default);
}
