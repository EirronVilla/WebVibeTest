using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebVibeTest.Infrastructure.Identity;

namespace WebVibeTest.Infrastructure.Data.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.ProfileImagePath).HasMaxLength(300);
        builder.Property(profile => profile.ProfileImageContentType).HasMaxLength(50);
        builder.HasOne<IdentityUser>()
            .WithOne()
            .HasForeignKey<UserProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
