namespace WebVibeTest.Domain.Games;

public sealed class TradeOffer
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public required string ProposerUserId { get; set; }
    public TradeStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TurnNumber { get; set; }
    public int OfferedBrick { get; set; }
    public int OfferedLumber { get; set; }
    public int OfferedWool { get; set; }
    public int OfferedGrain { get; set; }
    public int OfferedOre { get; set; }
    public int RequestedBrick { get; set; }
    public int RequestedLumber { get; set; }
    public int RequestedWool { get; set; }
    public int RequestedGrain { get; set; }
    public int RequestedOre { get; set; }
    public Game Game { get; set; } = null!;
    public ICollection<TradeResponse> Responses { get; set; } = new List<TradeResponse>();
}

public sealed class TradeResponse
{
    public Guid Id { get; set; }
    public Guid TradeOfferId { get; set; }
    public required string UserId { get; set; }
    public TradeResponseStatus Status { get; set; }
    public DateTime? RespondedAt { get; set; }
    public TradeOffer TradeOffer { get; set; } = null!;
}
