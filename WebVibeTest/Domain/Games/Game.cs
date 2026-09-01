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
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
