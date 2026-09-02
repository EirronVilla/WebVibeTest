using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace WebVibeTest.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public sealed class InputModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        await signInManager.SignOutAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var username = Input.Username.Trim();
        var user = await userManager.FindByNameAsync(username);
        if (user is null && !username.Contains('@'))
        {
            // Accounts created by the original template used the email address as UserName.
            // Let an unambiguous legacy account sign in with the portion before '@'.
            var normalizedPrefix = userManager.NormalizeName(username) + "@";
            var legacyMatches = await userManager.Users
                .Where(candidate => candidate.NormalizedUserName != null && candidate.NormalizedUserName.StartsWith(normalizedPrefix))
                .Take(2)
                .ToListAsync();
            if (legacyMatches.Count == 1) user = legacyMatches[0];
        }
        if (user is not null)
        {
            var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded) return LocalRedirect(returnUrl ?? Url.Content("~/"));
            if (result.IsLockedOut) ModelState.AddModelError(string.Empty, "This account is temporarily locked. Please try again later.");
            else ModelState.AddModelError(string.Empty, "Invalid username or password.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
        }

        return Page();
    }
}
