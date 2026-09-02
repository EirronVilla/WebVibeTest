using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using WebVibeTest.Domain.Games;
using WebVibeTest.Infrastructure.Identity;

namespace WebVibeTest.Infrastructure.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<Game> Games => Set<Game>();
        public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
        public DbSet<TradeOffer> TradeOffers => Set<TradeOffer>();
        public DbSet<TradeResponse> TradeResponses => Set<TradeResponse>();
        public DbSet<DevelopmentCard> DevelopmentCards => Set<DevelopmentCard>();
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}
