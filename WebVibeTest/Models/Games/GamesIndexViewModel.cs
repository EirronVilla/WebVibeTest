namespace WebVibeTest.Models.Games;

public sealed class GamesIndexViewModel
{
    public IReadOnlyList<AvailableGameViewModel> Games { get; init; } = [];
    public IReadOnlyList<ActiveGameViewModel> ActiveGames { get; init; } = [];
    public JoinPrivateGameViewModel PrivateJoin { get; init; } = new();
}

public sealed class ActiveGameViewModel
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string HostName { get; init; }
    public int PlayerCount { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public bool IsCurrentUserHost { get; init; }
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
