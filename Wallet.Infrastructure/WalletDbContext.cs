using Microsoft.EntityFrameworkCore;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var u = modelBuilder.Entity<User>();
        u.ToTable("Users");
        u.HasKey(x => x.Id);
        u.Property(x => x.Id).ValueGeneratedNever();
        u.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        u.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        u.Property(x => x.Email).HasMaxLength(254).IsRequired();
        u.Property(x => x.NormalizedEmail).HasMaxLength(254)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();
        u.HasIndex(x => x.NormalizedEmail).IsUnique();
        u.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        u.Property(x => x.Role).HasMaxLength(20).IsRequired();
        u.Property(x => x.RowVersion).IsRowVersion();
    }
}
