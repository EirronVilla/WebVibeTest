using WebVibeTest.Domain.Board;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public sealed record ActiveGameReadModel(
    Guid Id,
    string Name,
    GamePhase Phase,
    string CurrentPlayerName,
    bool IsCurrentPlayer,
    bool MustPlaceSettlement,
    int? LatestDie1,
    int? LatestDie2,
    ResourceInventory OwnResources,
    int RequiredDiscardCount,
    IReadOnlyList<PublicPlayerState> Players,
    IReadOnlyList<RobberyTarget> EligibleRobberyTargets,
    BoardState Board,
    IReadOnlySet<int> AvailableConstructionVertexIds,
    IReadOnlySet<int> ValidSettlementVertexIds,
    IReadOnlySet<int> ValidRoadEdgeIds,
    ConstructionReadModel Construction,
    TradingReadModel Trading,
    AwardsReadModel Awards,
    DevelopmentCardsReadModel DevelopmentCards);

public sealed record AwardsReadModel(string? LongestRoadHolderUserId, string? LongestRoadHolderName, int LongestRoadLength, string? LargestArmyHolderUserId, string? LargestArmyHolderName);

public sealed record ConstructionReadModel(
    int RoadsRemaining,
    int SettlementsRemaining,
    int CitiesRemaining,
    bool CanAffordRoad,
    bool CanAffordSettlement,
    bool CanAffordCity,
    IReadOnlySet<int> ValidRoadEdgeIds,
    IReadOnlySet<int> ValidSettlementVertexIds,
    IReadOnlySet<int> ValidCityVertexIds);

public sealed record ResourceInventory(int Brick, int Lumber, int Wool, int Grain, int Ore)
{
    public int Total => Brick + Lumber + Wool + Grain + Ore;

    public int Get(ResourceType resource) => resource switch
    {
        ResourceType.Brick => Brick,
        ResourceType.Lumber => Lumber,
        ResourceType.Wool => Wool,
        ResourceType.Grain => Grain,
        ResourceType.Ore => Ore,
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };
}

public sealed record PublicPlayerState(
    string UserId,
    string DisplayName,
    PlayerColor Color,
    int TotalResources,
    int VisibleVictoryPoints,
    int DevelopmentCardCount,
    bool IsCurrentPlayer);
public sealed record RobberyTarget(string UserId, string DisplayName);

public sealed record ResourceDiscard(int Brick, int Lumber, int Wool, int Grain, int Ore)
{
    public int Total => Brick + Lumber + Wool + Grain + Ore;
}

public sealed record ProductionSummary(string UserId, int CardsProduced);
public sealed record DiceRollResult(int Die1, int Die2, IReadOnlyList<ProductionSummary> Production, bool RequiresDiscards);
public sealed record RobberMoveResult(int HexId, bool RequiresTarget);
public sealed record RobberyResult(string TargetUserId);
public sealed record TurnChangeResult(string CurrentPlayerUserId, IReadOnlyList<Guid> CancelledTradeOfferIds);
public sealed record BuildResult(string BuildingType, int LocationId, string UserId);

public sealed record ResourceBundle(int Brick, int Lumber, int Wool, int Grain, int Ore)
{
    public int Total => Brick + Lumber + Wool + Grain + Ore;
    public bool HasNegative => Brick < 0 || Lumber < 0 || Wool < 0 || Grain < 0 || Ore < 0;
}

public sealed record TradingReadModel(
    IReadOnlyList<TradeOfferReadModel> Offers,
    IReadOnlyDictionary<ResourceType, int> MaritimeRates);

public sealed record TradeOfferReadModel(
    Guid Id,
    string ProposerName,
    bool IsProposer,
    ResourceBundle Offered,
    ResourceBundle Requested,
    TradeResponseStatus? OwnResponse,
    IReadOnlyList<TradeResponseReadModel> Responses);

public sealed record TradeResponseReadModel(string UserId, string DisplayName, TradeResponseStatus Status);
public sealed record TradeEventResult(Guid OfferId, IReadOnlyList<string> ParticipantUserIds);
public sealed record MaritimeTradeResult(ResourceType Given, int Rate, ResourceType Received);

public sealed record DevelopmentCardsReadModel(
    IReadOnlyList<DevelopmentCardReadModel> OwnCards,
    int DeckRemaining,
    ResourceInventory Bank,
    bool CanBuy,
    int ActualVictoryPoints,
    int KnightsPlayed,
    int FreeRoadsRemaining,
    IReadOnlySet<int> ValidFreeRoadEdgeIds);

public sealed record DevelopmentCardReadModel(Guid Id, DevelopmentCardType Type, bool CanPlay, bool IsNew);
public sealed record DevelopmentCardPurchaseResult(Guid CardId, DevelopmentCardType Type, string OwnerUserId);
public sealed record DevelopmentCardPlayResult(Guid CardId, DevelopmentCardType Type, string PlayerUserId);

public sealed record CompletedGameReadModel(
    Guid Id, string Name, string WinnerName, IReadOnlyList<FinalPlayerResult> Players,
    string? LongestRoadHolderName, string? LargestArmyHolderName);
public sealed record FinalPlayerResult(string DisplayName, int FinalVictoryPoints, int FinalRank, bool IsWinner, int RoadsBuilt, int SettlementsBuilt, int CitiesBuilt, int DevelopmentCardsBought, int DevelopmentCardsPlayed, int TotalResourcesGained);
public sealed record UserStatistics(int GamesPlayed, int Wins, decimal WinPercentage, int TotalVictoryPoints, decimal AverageVictoryPoints, decimal AverageFinishingPosition);
