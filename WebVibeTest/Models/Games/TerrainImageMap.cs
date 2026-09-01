using WebVibeTest.Domain.Board;

namespace WebVibeTest.Models.Games;

public static class TerrainImageMap
{
    private static readonly IReadOnlyDictionary<TerrainType, string> ImagePaths =
        new Dictionary<TerrainType, string>
        {
            [TerrainType.Pasture] = "~/img/terrain/sheep_terrain.png",
            [TerrainType.Mountains] = "~/img/terrain/rock_terrain.png",
            [TerrainType.Forest] = "~/img/terrain/wood_terrain.png",
            [TerrainType.Fields] = "~/img/terrain/wheat_terrain.png",
            [TerrainType.Hills] = "~/img/terrain/brick_terrain.png",
            [TerrainType.Desert] = "~/img/terrain/dessert_terrain.png"
        };

    public static string GetPath(TerrainType terrain) => ImagePaths[terrain];
}
