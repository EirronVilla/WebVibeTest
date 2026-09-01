using System.ComponentModel.DataAnnotations;

namespace WebVibeTest.Models.Games;

public sealed class CreateGameViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Game name")]
    public string Name { get; set; } = string.Empty;

    [Range(3, 6)]
    [Display(Name = "Maximum players")]
    public int MaxPlayers { get; set; } = 4;

    [Display(Name = "Private game")]
    public bool IsPrivate { get; set; }
}
