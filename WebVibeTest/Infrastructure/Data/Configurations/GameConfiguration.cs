using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Infrastructure.Data.Configurations;

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasKey(game => game.Id);
        builder.Property(game => game.Name).HasMaxLength(200).IsRequired();
        builder.Property(game => game.JoinCode).HasMaxLength(12);
        builder.Property(game => game.HostUserId).IsRequired();
        builder.Property(game => game.BoardStateJson).HasColumnType("jsonb");
        builder.HasIndex(game => game.JoinCode).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint("CK_Games_MaxPlayers", "\"MaxPlayers\" BETWEEN 3 AND 6"));

        builder.HasMany(game => game.Players)
            .WithOne(player => player.Game)
            .HasForeignKey(player => player.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(game => game.HostUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(game => game.CurrentPlayerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
