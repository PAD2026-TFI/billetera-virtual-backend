using Microsoft.EntityFrameworkCore;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<global::Wallet.Domain.Wallet> Wallets => Set<global::Wallet.Domain.Wallet>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var w = modelBuilder.Entity<global::Wallet.Domain.Wallet>();
        w.ToTable("Wallets", table =>
        {
            table.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0");
            table.HasCheckConstraint("CK_Wallets_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });
        w.HasKey(x => x.Id);
        w.Property(x => x.Id).ValueGeneratedNever();
        w.Property(x => x.Balance).HasPrecision(18, 2);
        w.Property(x => x.Currency).HasColumnType("char(3)").HasMaxLength(3).IsRequired();
        w.Property(x => x.RowVersion).IsRowVersion();
        w.HasIndex(x => x.UserId).IsUnique();
        w.HasOne<User>().WithOne().HasForeignKey<global::Wallet.Domain.Wallet>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

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
