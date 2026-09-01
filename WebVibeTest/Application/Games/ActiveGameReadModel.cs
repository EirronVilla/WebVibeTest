using WebVibeTest.Domain.Board;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public sealed record ActiveGameReadModel(
    Guid Id,
    string Name,
    GamePhase Phase,
    string CurrentPlayerName,
    bool IsCurrentPlayer,
    bool MustPlaceSettlement,
    BoardState Board,
    IReadOnlySet<int> ValidSettlementVertexIds,
    IReadOnlySet<int> ValidRoadEdgeIds);
