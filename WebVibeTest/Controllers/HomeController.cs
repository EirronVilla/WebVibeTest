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
    ApplicationDbContext dbContext) : Controller
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
        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        if (picture.Length > maxLength || !allowedTypes.Contains(picture.ContentType))
        {
            TempData["ProfileError"] = "Upload a JPG, PNG, or WebP image no larger than 5 MB.";
            return RedirectToAction(nameof(Index));
        }

        var userId = userManager.GetUserId(User) ?? throw new UnauthorizedAccessException();
        await using var stream = new MemoryStream((int)picture.Length);
        await picture.CopyToAsync(stream, cancellationToken);
        var imageData = stream.ToArray();
        var imagePath = Url.Action(nameof(ProfilePicture), new { id = userId })!;

        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (profile is null)
        {
            dbContext.UserProfiles.Add(new UserProfile
            {
                UserId = userId,
                ProfileImagePath = imagePath,
                ProfileImageData = imageData,
                ProfileImageContentType = picture.ContentType
            });
        }
        else
        {
            profile.ProfileImagePath = imagePath;
            profile.ProfileImageData = imageData;
            profile.ProfileImageContentType = picture.ContentType;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["ProfileMessage"] = "Portrait updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ProfilePicture(string id, CancellationToken cancellationToken)
    {
        var image = await dbContext.UserProfiles.AsNoTracking()
            .Where(profile => profile.UserId == id && profile.ProfileImageData != null)
            .Select(profile => new { profile.ProfileImageData, profile.ProfileImageContentType })
            .SingleOrDefaultAsync(cancellationToken);

        if (image?.ProfileImageData is null) return NotFound();
        return File(image.ProfileImageData, image.ProfileImageContentType ?? "application/octet-stream");
    }

    [AllowAnonymous]
    public IActionResult Privacy() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    private static string DisplayUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? "Player" : username.Split('@', 2)[0];
}
