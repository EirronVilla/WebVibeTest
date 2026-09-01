namespace WebVibeTest.Models.Games;

public sealed class GamesIndexViewModel
{
    public IReadOnlyList<AvailableGameViewModel> Games { get; init; } = [];
    public JoinPrivateGameViewModel PrivateJoin { get; init; } = new();
}

public sealed class AvailableGameViewModel
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string HostName { get; init; }
    public int PlayerCount { get; init; }
    public int MaxPlayers { get; init; }
    public bool IsMember { get; init; }
}
