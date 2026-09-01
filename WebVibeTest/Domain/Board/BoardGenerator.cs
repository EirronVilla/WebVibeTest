namespace WebVibeTest.Domain.Board;

public static class BoardGenerator
{
    private static readonly int[] StandardNumberSequence =
        [5, 2, 6, 3, 8, 10, 9, 12, 11, 4, 8, 10, 9, 4, 5, 6, 3, 11];

    private static readonly int[] ExtensionNumberSequence =
        [2, 5, 4, 6, 3, 9, 8, 11, 11, 10, 6, 3, 8, 4, 8, 10, 11, 12, 10, 5, 4, 9, 5, 9, 12, 3, 2, 6];

    public static BoardState Generate(int seed, int playerCount)
    {
        if (playerCount is < 3 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        }

        var extended = playerCount >= 5;
        var coordinates = extended ? ExtensionCoordinates() : StandardCoordinates();
        var terrains = extended ? ExtensionTerrains() : StandardTerrains();
        var random = new StableRandom(unchecked((uint)seed));
        Shuffle(terrains, random);

        var tokens = extended ? ExtensionNumberSequence : StandardNumberSequence;
        var tokenByCoordinate = AssignNumberTokens(coordinates, terrains, tokens);
        var vertices = new List<BoardVertex>();
        var edges = new List<BoardEdge>();
        var vertexKeys = new Dictionary<(long X, long Y), int>();
        var edgeKeys = new Dictionary<(int A, int B), int>();
        var hexes = new List<BoardHex>();

        for (var hexIndex = 0; hexIndex < coordinates.Count; hexIndex++)
        {
            var (q, r) = coordinates[hexIndex];
            var centerX = Math.Sqrt(3) * (q + r / 2d);
            var centerY = 1.5 * r;
            var hexVertexIds = new int[6];
            var hexEdgeIds = new int[6];

            for (var corner = 0; corner < 6; corner++)
            {
                var angle = Math.PI / 180 * (60 * corner - 30);
                var x = centerX + Math.Cos(angle);
                var y = centerY + Math.Sin(angle);
                var key = ((long)Math.Round(x * 1_000_000), (long)Math.Round(y * 1_000_000));
                if (!vertexKeys.TryGetValue(key, out var vertexId))
                {
                    vertexId = vertices.Count;
                    vertexKeys.Add(key, vertexId);
                    vertices.Add(new BoardVertex
                    {
                        Id = vertexId,
                        X = Math.Round(x, 6),
                        Y = Math.Round(y, 6),
                        AdjacentVertexIds = [],
                        EdgeIds = []
                    });
                }

                hexVertexIds[corner] = vertexId;
            }

            for (var side = 0; side < 6; side++)
            {
                var a = hexVertexIds[side];
                var b = hexVertexIds[(side + 1) % 6];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeKeys.TryGetValue(key, out var edgeId))
                {
                    edgeId = edges.Count;
                    edgeKeys.Add(key, edgeId);
                    edges.Add(new BoardEdge { Id = edgeId, VertexAId = key.Item1, VertexBId = key.Item2 });
                    vertices[key.Item1].AdjacentVertexIds.Add(key.Item2);
                    vertices[key.Item2].AdjacentVertexIds.Add(key.Item1);
                    vertices[key.Item1].EdgeIds.Add(edgeId);
                    vertices[key.Item2].EdgeIds.Add(edgeId);
                }

                hexEdgeIds[side] = edgeId;
            }

            hexes.Add(new BoardHex
            {
                Id = hexIndex,
                Q = q,
                R = r,
                Terrain = terrains[hexIndex],
                NumberToken = tokenByCoordinate.GetValueOrDefault(hexIndex),
                VertexIds = hexVertexIds,
                EdgeIds = hexEdgeIds
            });
        }

        var coastalEdgeIds = edges
            .Where(edge => hexes.Count(hex => hex.EdgeIds.Contains(edge.Id)) == 1)
            .OrderBy(edge => Math.Atan2(
                (vertices[edge.VertexAId].Y + vertices[edge.VertexBId].Y) / 2,
                (vertices[edge.VertexAId].X + vertices[edge.VertexBId].X) / 2))
            .Select(edge => edge.Id)
            .ToList();
        var portTypes = extended
            ? new[] { PortType.Generic, PortType.Brick, PortType.Generic, PortType.Lumber, PortType.Generic, PortType.Ore, PortType.Generic, PortType.Grain, PortType.Generic, PortType.Wool, PortType.Generic }
            : new[] { PortType.Generic, PortType.Brick, PortType.Generic, PortType.Lumber, PortType.Generic, PortType.Ore, PortType.Grain, PortType.Wool, PortType.Generic };
        var ports = portTypes.Select((type, index) => new BoardPort
        {
            Id = index,
            EdgeId = coastalEdgeIds[index * coastalEdgeIds.Count / portTypes.Length],
            Type = type
        }).ToList();

        return new BoardState
        {
            Hexes = hexes,
            Vertices = vertices,
            Edges = edges,
            Ports = ports,
            RobberHexId = hexes.First(hex => hex.Terrain == TerrainType.Desert).Id
        };
    }

    private static Dictionary<int, int?> AssignNumberTokens(
        IReadOnlyList<(int Q, int R)> coordinates,
        IReadOnlyList<TerrainType> terrains,
        IReadOnlyList<int> tokens)
    {
        var spiral = coordinates
            .Select((coordinate, index) => new { coordinate, index })
            .OrderByDescending(item => HexDistance(item.coordinate.Q, item.coordinate.R))
            .ThenBy(item => Math.Atan2(1.5 * item.coordinate.R, Math.Sqrt(3) * (item.coordinate.Q + item.coordinate.R / 2d)))
            .Select(item => item.index)
            .ToList();
        var result = coordinates.Select((_, index) => index).ToDictionary(index => index, _ => (int?)null);
        var tokenIndex = 0;
        foreach (var hexIndex in spiral)
        {
            if (terrains[hexIndex] != TerrainType.Desert)
            {
                result[hexIndex] = tokens[tokenIndex++];
            }
        }

        return result;
    }

    private static int HexDistance(int q, int r) => Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r)));

    private static List<(int Q, int R)> StandardCoordinates() => AxialRows([3, 4, 5, 4, 3]);
    private static List<(int Q, int R)> ExtensionCoordinates() => AxialRows([3, 4, 5, 6, 5, 4, 3]);

    private static List<(int Q, int R)> AxialRows(IReadOnlyList<int> rowLengths)
    {
        var result = new List<(int, int)>();
        var center = rowLengths.Count / 2;
        for (var row = 0; row < rowLengths.Count; row++)
        {
            var r = row - center;
            var startQ = row <= center ? -row : -center;
            for (var column = 0; column < rowLengths[row]; column++)
            {
                result.Add((startQ + column, r));
            }
        }

        return result;
    }

    private static List<TerrainType> StandardTerrains() =>
        Repeat(TerrainType.Forest, 4, TerrainType.Pasture, 4, TerrainType.Fields, 4, TerrainType.Hills, 3, TerrainType.Mountains, 3, TerrainType.Desert, 1);

    private static List<TerrainType> ExtensionTerrains() =>
        Repeat(TerrainType.Forest, 6, TerrainType.Pasture, 6, TerrainType.Fields, 6, TerrainType.Hills, 5, TerrainType.Mountains, 5, TerrainType.Desert, 2);

    private static List<TerrainType> Repeat(params object[] values)
    {
        var result = new List<TerrainType>();
        for (var index = 0; index < values.Length; index += 2)
        {
            result.AddRange(Enumerable.Repeat((TerrainType)values[index], (int)values[index + 1]));
        }

        return result;
    }

    private static void Shuffle<T>(IList<T> values, StableRandom random)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private sealed class StableRandom(uint state)
    {
        private uint _state = state == 0 ? 0x9E3779B9u : state;

        public int Next(int exclusiveMaximum)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
