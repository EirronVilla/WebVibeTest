using WebVibeTest.Application.Games;
using Xunit;

namespace WebVibeTest.Tests;

public sealed class PairedTurnRulesTests
{
    [Fact]
    public void FivePlayerSecondaryIsThreeSeatsLeft() => Assert.Equal(3, PairedTurnRules.SecondaryPlayerIndex(0, 5));

    [Fact]
    public void SixPlayerSecondaryIsThreeSeatsLeft() => Assert.Equal(3, PairedTurnRules.SecondaryPlayerIndex(0, 6));

    [Theory]
    [InlineData(0, 5, 3)]
    [InlineData(1, 5, 4)]
    [InlineData(2, 5, 0)]
    [InlineData(3, 6, 0)]
    [InlineData(5, 6, 2)]
    public void SecondarySelectionWrapsInTurnOrder(int primary, int players, int expected) =>
        Assert.Equal(expected, PairedTurnRules.SecondaryPlayerIndex(primary, players));

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ThreeAndFourPlayerGamesDoNotUsePairedActions(int players) => Assert.False(PairedTurnRules.UsesPairedActions(players));
}
