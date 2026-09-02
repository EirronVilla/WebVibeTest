using WebVibeTest.Domain.Board;

namespace WebVibeTest.Models.Games;

public sealed record PortSvgPresentation(string Rate, string Detail, string CssClass, string AccessibleName);

public sealed record PortSvgLayout(
    double VertexAX,
    double VertexAY,
    double VertexBX,
    double VertexBY,
    double MarkerX,
    double MarkerY,
    PortSvgPresentation Presentation);

/// <summary>Central mapping and geometry for board port visuals.</summary>
public static class PortSvgMap
{
    public const double MarkerOffset = 0.55;
    public const double BadgeHalfWidth = 0.43;
    public const double BadgeHalfHeight = 0.29;

    public static PortSvgLayout CreateLayout(BoardState board, BoardPort port)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(port);
        if (port.VertexIds.Length != 2) throw new InvalidOperationException("A port must reference exactly two vertices.");

        var a = board.Vertices[port.VertexIds[0]];
        var b = board.Vertices[port.VertexIds[1]];
        var midpointX = (a.X + b.X) / 2;
        var midpointY = (a.Y + b.Y) / 2;
        var centerX = (board.Vertices.Min(vertex => vertex.X) + board.Vertices.Max(vertex => vertex.X)) / 2;
        var centerY = (board.Vertices.Min(vertex => vertex.Y) + board.Vertices.Max(vertex => vertex.Y)) / 2;
        var outwardX = midpointX - centerX;
        var outwardY = midpointY - centerY;
        var length = Math.Sqrt(outwardX * outwardX + outwardY * outwardY);
        if (length <= double.Epsilon) throw new InvalidOperationException("A coastal port cannot be positioned at the board center.");

        return new PortSvgLayout(
            a.X,
            a.Y,
            b.X,
            b.Y,
            midpointX + outwardX / length * MarkerOffset,
            midpointY + outwardY / length * MarkerOffset,
            GetPresentation(port.Type));
    }

    public static PortSvgPresentation GetPresentation(PortType type) => type switch
    {
        PortType.Generic => new("3:1", "⚓", "generic", "Generic three-to-one port"),
        PortType.Brick => new("2:1", "BRICK", "brick", "Brick two-to-one port"),
        PortType.Lumber => new("2:1", "LUMBER", "lumber", "Lumber two-to-one port"),
        PortType.Wool => new("2:1", "WOOL", "wool", "Wool two-to-one port"),
        PortType.Grain => new("2:1", "GRAIN", "grain", "Grain two-to-one port"),
        PortType.Ore => new("2:1", "ORE", "ore", "Ore two-to-one port"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported port type.")
    };
}
