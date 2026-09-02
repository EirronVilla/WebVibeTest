namespace WebVibeTest.Infrastructure.Identity;

public sealed class UserProfile
{
    public required string UserId { get; set; }
    public string? ProfileImagePath { get; set; }
}
