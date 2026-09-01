namespace WebVibeTest.Domain.Games;

public sealed class DevelopmentCard
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public required string OwnerUserId { get; set; }
    public DevelopmentCardType Type { get; set; }
    public int PurchasedTurnNumber { get; set; }
    public DateTime PurchasedAt { get; set; }
    public DateTime? PlayedAt { get; set; }
    public Game Game { get; set; } = null!;
}
