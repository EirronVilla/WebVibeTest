using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Infrastructure.Data.Configurations;

public sealed class GamePlayerConfiguration : IEntityTypeConfiguration<GamePlayer>
{
    public void Configure(EntityTypeBuilder<GamePlayer> builder)
    {
        builder.HasKey(player => player.Id);
        builder.Property(player => player.UserId).IsRequired();
        builder.Ignore(player => player.TotalResources);
        builder.HasIndex(player => new { player.GameId, player.UserId }).IsUnique();
        builder.HasIndex(player => new { player.GameId, player.TurnOrder }).IsUnique();

        builder.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(player => player.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
