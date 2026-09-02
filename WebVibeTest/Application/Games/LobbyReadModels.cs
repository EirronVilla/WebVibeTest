using WebVibeTest.Domain.Games;

namespace WebVibeTest.Application.Games;

public sealed record AvailableGame(
    Guid Id,
    string Name,
    string HostName,
    int PlayerCount,
    int MaxPlayers,
    bool IsMember);

public sealed record WaitingLobby(
    Guid Id,
    string Name,
    string HostName,
    int MaxPlayers,
    bool IsPrivate,
    string? JoinCode,
    bool IsCurrentUserHost,
    bool CanStart,
    bool ColorsAreUnique,
    IReadOnlyList<WaitingLobbyPlayer> Players);

public sealed record WaitingLobbyPlayer(
    string DisplayName,
    PlayerColor Color,
    bool IsHost,
    bool IsCurrentUser);
