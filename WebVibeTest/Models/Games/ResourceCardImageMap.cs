using WebVibeTest.Domain.Games;

namespace WebVibeTest.Models.Games;

public static class ResourceCardImageMap
{
    private static readonly IReadOnlyDictionary<ResourceType, string> ImagePaths =
        new Dictionary<ResourceType, string>
        {
            [ResourceType.Brick] = "~/img/cards/brick_card.png",
            [ResourceType.Lumber] = "~/img/cards/wood_card.png",
            [ResourceType.Wool] = "~/img/cards/sheep_card.png",
            [ResourceType.Grain] = "~/img/cards/wheat_card.png",
            [ResourceType.Ore] = "~/img/cards/rock_card.png"
        };

    public static string GetPath(ResourceType resource) => ImagePaths[resource];
}
