using WebVibeTest.Domain.Games;

namespace WebVibeTest.Models.Games;

public static class BoardPieceAssetMap
{
    public const string RobberPath = "~/img/assets/robber.png";

    private static readonly HashSet<PlayerColor> SupportedColors =
        [PlayerColor.Red, PlayerColor.Blue, PlayerColor.White, PlayerColor.Yellow, PlayerColor.Green, PlayerColor.Black];

    public static string? GetBuildingPath(PlayerColor color, BuildingType buildingType) =>
        SupportedColors.Contains(color)
            ? $"~/img/assets/{color.ToString().ToLowerInvariant()}_{buildingType.ToString().ToLowerInvariant()}.png"
            : null;

}
