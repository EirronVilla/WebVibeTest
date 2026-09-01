namespace WebVibeTest.Domain.Games;

public sealed class GamePlayer
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public required string UserId { get; set; }
    public int TurnOrder { get; set; }
    public PlayerColor Color { get; set; }
    public bool IsHost { get; set; }
    public DateTime JoinedAt { get; set; }
    public Game Game { get; set; } = null!;
}
