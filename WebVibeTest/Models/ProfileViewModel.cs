using WebVibeTest.Application.Games;

namespace WebVibeTest.Models;

public sealed class ProfileViewModel
{
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? ProfileImagePath { get; init; }
    public required UserStatistics Statistics { get; init; }
}
