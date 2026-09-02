namespace WebVibeTest.Domain.Games;

public sealed class Game
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public GameStatus Status { get; set; }
    public int MaxPlayers { get; set; }
    public bool IsPrivate { get; set; }
    public string? JoinCode { get; set; }
    public required string HostUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? BoardSeed { get; set; }
    public string? BoardStateJson { get; set; }
    public GamePhase? Phase { get; set; }
    public string? CurrentPlayerUserId { get; set; }
    public int? PendingSettlementVertexId { get; set; }
    public int? LatestDie1 { get; set; }
    public int? LatestDie2 { get; set; }
    public string? DevelopmentDeckJson { get; set; }
    public string? ResourceBankJson { get; set; }
    public int TurnNumber { get; set; }
    public bool DevelopmentCardPlayedThisTurn { get; set; }
    public int FreeRoadsRemaining { get; set; }
    public string? LongestRoadHolderUserId { get; set; }
    public int LongestRoadLength { get; set; }
    public string? LargestArmyHolderUserId { get; set; }
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
