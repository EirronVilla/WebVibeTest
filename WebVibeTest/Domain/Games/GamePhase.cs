namespace WebVibeTest.Domain.Games;

public enum GamePhase
{
    InitialPlacementForward,
    InitialPlacementReverse,
    TurnProduction,
    AwaitingDiscards,
    AwaitingRobberPlacement,
    AwaitingRobberyTarget,
    AwaitingRoadBuilding,
    TurnActions,
    Completed
}
