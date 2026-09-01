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
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
