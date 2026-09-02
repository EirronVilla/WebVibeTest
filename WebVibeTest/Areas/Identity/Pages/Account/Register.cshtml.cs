using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebVibeTest.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class RegisterModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public sealed class InputModel
    {
        [Required, StringLength(40, MinimumLength = 3)]
        [RegularExpression(@"^[A-Za-z0-9_.-]+$", ErrorMessage = "Use letters, numbers, dots, dashes, or underscores only.")]
        public string Username { get; set; } = string.Empty;

        [EmailAddress]
        [Display(Name = "Email (optional)")]
        public string? Email { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        var user = new IdentityUser { UserName = Input.Username.Trim(), Email = email };
        var result = await userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            await signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        return Page();
    }
}
