namespace WebVibeTest.Application.Games;

public static class PairedTurnRules
{
    public static bool UsesPairedActions(int playerCount) => playerCount >= 5;

    public static int SecondaryPlayerIndex(int primaryPlayerIndex, int playerCount)
    {
        if (!UsesPairedActions(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
        if (primaryPlayerIndex < 0 || primaryPlayerIndex >= playerCount) throw new ArgumentOutOfRangeException(nameof(primaryPlayerIndex));
        return (primaryPlayerIndex + 3) % playerCount;
    }
}
