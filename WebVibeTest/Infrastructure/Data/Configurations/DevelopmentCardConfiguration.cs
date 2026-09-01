using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Infrastructure.Data.Configurations;

public sealed class DevelopmentCardConfiguration : IEntityTypeConfiguration<DevelopmentCard>
{
    public void Configure(EntityTypeBuilder<DevelopmentCard> builder)
    {
        builder.HasKey(card => card.Id);
        builder.HasOne(card => card.Game).WithMany().HasForeignKey(card => card.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(card => card.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(card => new { card.GameId, card.OwnerUserId });
    }
}
