using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebVibeTest.Application.Games;
using WebVibeTest.Infrastructure.Data;
using WebVibeTest.Infrastructure.Identity;
using WebVibeTest.Models;

namespace WebVibeTest.Controllers;

[Authorize]
public sealed class HomeController(
    UserManager<IdentityUser> userManager,
    IGameService gameService,
    ApplicationDbContext dbContext,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
        var profile = await dbContext.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        return View(new ProfileViewModel
        {
            Username = DisplayUsername(user.UserName),
            Email = user.Email,
            ProfileImagePath = profile?.ProfileImagePath,
            Statistics = await gameService.GetStatisticsAsync(user.Id, cancellationToken)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPicture(IFormFile picture, CancellationToken cancellationToken)
    {
        if (picture is null || picture.Length == 0)
        {
            TempData["ProfileError"] = "Choose an image to upload.";
            return RedirectToAction(nameof(Index));
        }

        const long maxLength = 5 * 1024 * 1024;
        var allowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp"
        };

        if (picture.Length > maxLength || !allowedTypes.TryGetValue(picture.ContentType, out var extension))
        {
            TempData["ProfileError"] = "Upload a JPG, PNG, or WebP image no larger than 5 MB.";
            return RedirectToAction(nameof(Index));
        }

        var userId = userManager.GetUserId(User) ?? throw new UnauthorizedAccessException();
        var relativePath = $"/uploads/profiles/{userId}-{Guid.NewGuid():N}{extension}";
        var directory = Path.Combine(environment.WebRootPath, "uploads", "profiles");
        Directory.CreateDirectory(directory);
        var absolutePath = Path.Combine(environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

        await using (var stream = new FileStream(absolutePath, FileMode.CreateNew))
        {
            await picture.CopyToAsync(stream, cancellationToken);
        }

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile { UserId = userId, ProfileImagePath = relativePath });
        }
        else
        {
            profile.ProfileImagePath = relativePath;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["ProfileMessage"] = "Portrait updated.";
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous]
    public IActionResult Privacy() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    private static string DisplayUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? "Player" : username.Split('@', 2)[0];
}
