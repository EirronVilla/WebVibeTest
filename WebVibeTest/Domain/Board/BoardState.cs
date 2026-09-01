using WebVibeTest.Domain.Games;

namespace WebVibeTest.Domain.Board;

public sealed class BoardState
{
    public required List<BoardHex> Hexes { get; init; }
    public required List<BoardVertex> Vertices { get; init; }
    public required List<BoardEdge> Edges { get; init; }
    public required List<BoardPort> Ports { get; init; }
    public int RobberHexId { get; set; }
}

public sealed class BoardHex
{
    public int Id { get; init; }
    public int Q { get; init; }
    public int R { get; init; }
    public TerrainType Terrain { get; init; }
    public int? NumberToken { get; init; }
    public required int[] VertexIds { get; init; }
    public required int[] EdgeIds { get; init; }
}

public sealed class BoardVertex
{
    public int Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public required List<int> AdjacentVertexIds { get; init; }
    public required List<int> EdgeIds { get; init; }
    public SettlementState? Settlement { get; set; }
}

public sealed class BoardEdge
{
    public int Id { get; init; }
    public int VertexAId { get; init; }
    public int VertexBId { get; init; }
    public RoadState? Road { get; set; }
}

public sealed class BoardPort
{
    public int Id { get; init; }
    public int EdgeId { get; init; }
    public PortType Type { get; init; }
    public int[] VertexIds { get; init; } = [];
}

public sealed class SettlementState
{
    public required string UserId { get; init; }
    public PlayerColor Color { get; init; }
    public BuildingType BuildingType { get; set; }
    public int ProductionAmount => BuildingType == BuildingType.City ? 2 : 1;
}

public sealed class RoadState
{
    public required string UserId { get; init; }
    public PlayerColor Color { get; init; }
}
