using System;
using System.Linq;
using WebVibeTest.Domain.Board;
using WebVibeTest.Models.Games;
using Xunit;

namespace WebVibeTest.Tests;

public sealed class BoardSvgBoundsTests
{
    [Theory]
    [InlineData(4, 19)]
    [InlineData(6, 30)]
    public void ViewBoxContainsFullTopologyAndRenderingMargin(int playerCount, int expectedHexes)
    {
        var board = BoardGenerator.Generate(123456, playerCount);
        var bounds = BoardSvgBounds.From(board);

        Assert.Equal(expectedHexes, board.Hexes.Count);
        Assert.All(board.Vertices, vertex =>
        {
            Assert.True(vertex.X - BoardSvgBounds.RenderMargin >= bounds.MinX - 0.000001);
            Assert.True(vertex.X + BoardSvgBounds.RenderMargin <= bounds.MaxX + 0.000001);
            Assert.True(vertex.Y - BoardSvgBounds.RenderMargin >= bounds.MinY - 0.000001);
            Assert.True(vertex.Y + BoardSvgBounds.RenderMargin <= bounds.MaxY + 0.000001);
        });
    }

    [Fact]
    public void ExtensionViewBoxExpandsForLargerTopology()
    {
        var standard = BoardSvgBounds.From(BoardGenerator.Generate(1, 4));
        var extension = BoardSvgBounds.From(BoardGenerator.Generate(1, 6));

        Assert.True(extension.Width > standard.Width);
        Assert.True(extension.Height > standard.Height);
    }

    [Theory]
    [InlineData(4, 9)]
    [InlineData(6, 11)]
    public void EveryPortConnectsTwoCoastalVerticesAndFitsTheViewBox(int playerCount, int expectedPorts)
    {
        var board = BoardGenerator.Generate(98765, playerCount);
        var bounds = BoardSvgBounds.From(board);

        Assert.Equal(expectedPorts, board.Ports.Count);
        foreach (var port in board.Ports)
        {
            Assert.Equal(2, port.VertexIds.Length);
            Assert.NotEqual(port.VertexIds[0], port.VertexIds[1]);
            var edge = board.Edges[port.EdgeId];
            Assert.Equal([edge.VertexAId, edge.VertexBId], port.VertexIds.OrderBy(id => id).ToArray());

            var layout = PortSvgMap.CreateLayout(board, port);
            Assert.InRange(layout.MarkerX - PortSvgMap.BadgeHalfWidth, bounds.MinX, bounds.MaxX);
            Assert.InRange(layout.MarkerX + PortSvgMap.BadgeHalfWidth, bounds.MinX, bounds.MaxX);
            Assert.InRange(layout.MarkerY - PortSvgMap.BadgeHalfHeight, bounds.MinY, bounds.MaxY);
            Assert.InRange(layout.MarkerY + PortSvgMap.BadgeHalfHeight, bounds.MinY, bounds.MaxY);
        }
    }

    [Fact]
    public void PortPresentationUsesOfficialRates()
    {
        Assert.Equal("3:1", PortSvgMap.GetPresentation(PortType.Generic).Rate);
        foreach (var type in Enum.GetValues<PortType>().Where(type => type != PortType.Generic))
        {
            Assert.Equal("2:1", PortSvgMap.GetPresentation(type).Rate);
        }
    }
}
