namespace WebVibeTest.Infrastructure.Files;

public sealed class ProfileImageStorage(string rootPath)
{
    public string RootPath { get; } = rootPath;
    public const string RequestPath = "/uploads/profiles";
}
