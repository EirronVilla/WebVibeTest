using WebVibeTest.Domain.Board;

namespace WebVibeTest.Models.Games;

/// <summary>
/// Bounds for the complete rendered board, including artwork and interactive
/// markers that extend beyond the mathematical vertex topology.
/// </summary>
public readonly record struct BoardSvgBounds(double MinX, double MinY, double Width, double Height)
{
    // The largest current overflow is a city image above a vertex. Keeping the
    // same generous margin on every side also leaves room for road strokes,
    // settlement art, and placement hit targets without layout-specific offsets.
    public const double RenderMargin = 0.9;

    public double MaxX => MinX + Width;
    public double MaxY => MinY + Height;

    public static BoardSvgBounds From(BoardState board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.Vertices.Count == 0) throw new ArgumentException("The board has no vertices.", nameof(board));

        var topologyMinX = board.Vertices.Min(vertex => vertex.X);
        var topologyMaxX = board.Vertices.Max(vertex => vertex.X);
        var topologyMinY = board.Vertices.Min(vertex => vertex.Y);
        var topologyMaxY = board.Vertices.Max(vertex => vertex.Y);

        return new BoardSvgBounds(
            topologyMinX - RenderMargin,
            topologyMinY - RenderMargin,
            topologyMaxX - topologyMinX + RenderMargin * 2,
            topologyMaxY - topologyMinY + RenderMargin * 2);
    }
}
