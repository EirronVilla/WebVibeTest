using WebVibeTest.Domain.Games;

namespace WebVibeTest.Models.Games;

public sealed class LobbyViewModel
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string HostName { get; init; }
    public int MaxPlayers { get; init; }
    public bool IsPrivate { get; init; }
    public string? JoinCode { get; init; }
    public bool IsCurrentUserHost { get; init; }
    public bool CanStart { get; init; }
    public bool ColorsAreUnique { get; init; }
    public IReadOnlyList<LobbyPlayerViewModel> Players { get; init; } = [];
}

public sealed class LobbyPlayerViewModel
{
    public required string DisplayName { get; init; }
    public PlayerColor Color { get; init; }
    public bool IsHost { get; init; }
    public bool IsCurrentUser { get; init; }
}
