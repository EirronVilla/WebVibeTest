using System.ComponentModel.DataAnnotations;

namespace WebVibeTest.Models.Games;

public sealed class JoinPrivateGameViewModel
{
    [Required, StringLength(12, MinimumLength = 12)]
    [Display(Name = "Join code")]
    public string JoinCode { get; set; } = string.Empty;
}
