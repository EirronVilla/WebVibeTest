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
    public int Brick { get; set; }
    public int Lumber { get; set; }
    public int Wool { get; set; }
    public int Grain { get; set; }
    public int Ore { get; set; }
    public int RequiredDiscardCount { get; set; }
    public int VisibleVictoryPoints { get; set; }
    public int KnightsPlayed { get; set; }
    public Game Game { get; set; } = null!;

    public int TotalResources => Brick + Lumber + Wool + Grain + Ore;

    public int GetResource(ResourceType resource) => resource switch
    {
        ResourceType.Brick => Brick,
        ResourceType.Lumber => Lumber,
        ResourceType.Wool => Wool,
        ResourceType.Grain => Grain,
        ResourceType.Ore => Ore,
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };

    public void AddResource(ResourceType resource, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        switch (resource)
        {
            case ResourceType.Brick: Brick += amount; break;
            case ResourceType.Lumber: Lumber += amount; break;
            case ResourceType.Wool: Wool += amount; break;
            case ResourceType.Grain: Grain += amount; break;
            case ResourceType.Ore: Ore += amount; break;
            default: throw new ArgumentOutOfRangeException(nameof(resource));
        }
    }

    public void RemoveResource(ResourceType resource, int amount)
    {
        if (amount < 0 || GetResource(resource) < amount)
        {
            throw new InvalidOperationException("The player does not have those resource cards.");
        }

        switch (resource)
        {
            case ResourceType.Brick: Brick -= amount; break;
            case ResourceType.Lumber: Lumber -= amount; break;
            case ResourceType.Wool: Wool -= amount; break;
            case ResourceType.Grain: Grain -= amount; break;
            case ResourceType.Ore: Ore -= amount; break;
            default: throw new ArgumentOutOfRangeException(nameof(resource));
        }
    }
}
