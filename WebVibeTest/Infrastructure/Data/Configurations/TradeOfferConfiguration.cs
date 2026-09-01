using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebVibeTest.Domain.Games;

namespace WebVibeTest.Infrastructure.Data.Configurations;

public sealed class TradeOfferConfiguration : IEntityTypeConfiguration<TradeOffer>
{
    public void Configure(EntityTypeBuilder<TradeOffer> builder)
    {
        builder.HasKey(offer => offer.Id);
        builder.HasOne(offer => offer.Game).WithMany().HasForeignKey(offer => offer.GameId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(offer => offer.ProposerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(offer => new { offer.GameId, offer.Status });
    }
}

public sealed class TradeResponseConfiguration : IEntityTypeConfiguration<TradeResponse>
{
    public void Configure(EntityTypeBuilder<TradeResponse> builder)
    {
        builder.HasKey(response => response.Id);
        builder.HasOne(response => response.TradeOffer).WithMany(offer => offer.Responses).HasForeignKey(response => response.TradeOfferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(response => response.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(response => new { response.TradeOfferId, response.UserId }).IsUnique();
    }
}
