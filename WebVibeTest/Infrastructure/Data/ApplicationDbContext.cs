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

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();
            var hasRelatedAction = ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && entry.Entity is GamePlayer or TradeOffer or TradeResponse or DevelopmentCard);
            foreach (var entry in ChangeTracker.Entries<Game>().Where(entry =>
                entry.Entity.Status == GameStatus.InProgress
                && (hasRelatedAction || entry.State == EntityState.Modified)))
            {
                entry.Entity.ActionDeadlineUtc = DateTime.UtcNow.AddMinutes(1);
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
