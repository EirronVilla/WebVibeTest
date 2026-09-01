namespace WebVibeTest.Domain.Games;

public sealed class ResourceBank
{
    public int Brick { get; set; }
    public int Lumber { get; set; }
    public int Wool { get; set; }
    public int Grain { get; set; }
    public int Ore { get; set; }

    public int Get(ResourceType resource) => resource switch
    {
        ResourceType.Brick => Brick, ResourceType.Lumber => Lumber, ResourceType.Wool => Wool,
        ResourceType.Grain => Grain, ResourceType.Ore => Ore, _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };

    public void Add(ResourceType resource, int amount)
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

    public void Remove(ResourceType resource, int amount)
    {
        if (amount < 0 || Get(resource) < amount) throw new InvalidOperationException("The bank does not have enough resources.");
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
