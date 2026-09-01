using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using WebVibeTest.Domain.Games;

namespace WebVibeTest.Infrastructure.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<Game> Games => Set<Game>();
        public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
        public DbSet<TradeOffer> TradeOffers => Set<TradeOffer>();
        public DbSet<TradeResponse> TradeResponses => Set<TradeResponse>();
        public DbSet<DevelopmentCard> DevelopmentCards => Set<DevelopmentCard>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        }
    }
}
