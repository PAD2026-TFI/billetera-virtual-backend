using Microsoft.EntityFrameworkCore;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<global::Wallet.Domain.Wallet> Wallets => Set<global::Wallet.Domain.Wallet>();
    public DbSet<DevelopmentFunding> DevelopmentFundings => Set<DevelopmentFunding>();
    public DbSet<Movement> Movements => Set<Movement>();
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

        var f = modelBuilder.Entity<DevelopmentFunding>();
        f.ToTable("DevelopmentFundings", table =>
        {
            table.HasCheckConstraint("CK_DevelopmentFundings_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_DevelopmentFundings_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });
        f.HasKey(x => x.Id);
        f.Property(x => x.Id).ValueGeneratedNever();
        f.Property(x => x.Amount).HasPrecision(18, 2);
        f.Property(x => x.Currency).HasColumnType("char(3)").HasMaxLength(3).IsRequired();
        f.Property(x => x.RequestHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
        f.HasIndex(x => x.IdempotencyKey).IsUnique();
        f.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);

        var m = modelBuilder.Entity<Movement>();
        m.ToTable("Movements", table =>
        {
            table.HasCheckConstraint("CK_Movements_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Movements_BalanceAfter", "[BalanceAfter] >= 0");
            // Sólo esta fuente está implementada; TX y PAY ampliarán la restricción y sus FKs.
            table.HasCheckConstraint("CK_Movements_Source", "[DevelopmentFundingId] IS NOT NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit'");
        });
        m.HasKey(x => x.Id);
        m.Property(x => x.Id).ValueGeneratedNever();
        m.Property(x => x.Amount).HasPrecision(18, 2);
        m.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        m.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        m.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        m.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<DevelopmentFunding>().WithOne().HasForeignKey<Movement>(x => x.DevelopmentFundingId).OnDelete(DeleteBehavior.Restrict);
        m.HasIndex(x => new { x.WalletId, x.CreatedAt, x.Id });

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
