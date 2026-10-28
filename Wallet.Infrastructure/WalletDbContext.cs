using Microsoft.EntityFrameworkCore;
using Wallet.Domain;
namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<global::Wallet.Domain.Wallet> Wallets => Set<global::Wallet.Domain.Wallet>();
    public DbSet<DevelopmentFunding> DevelopmentFundings => Set<DevelopmentFunding>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
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

        var t = modelBuilder.Entity<Transfer>();
        t.ToTable("Transfers", table =>
        {
            table.HasCheckConstraint("CK_Transfers_Amount", "[Amount] > 0 AND [Amount] <= 780400");
            table.HasCheckConstraint("CK_Transfers_Wallets", "[SourceWalletId] <> [DestinationWalletId]");
            table.HasCheckConstraint("CK_Transfers_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
            table.HasCheckConstraint("CK_Transfers_Status", "[Status] = 'Completed'");
        });
        t.HasKey(x => x.Id); t.Property(x => x.Id).ValueGeneratedNever();
        t.Property(x => x.Amount).HasPrecision(18, 2);
        t.Property(x => x.Currency).HasColumnType("char(3)").HasMaxLength(3).IsRequired();
        t.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        t.Property(x => x.RequestHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
        t.HasIndex(x => new { x.SourceWalletId, x.IdempotencyKey }).IsUnique();
        t.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.SourceWalletId).OnDelete(DeleteBehavior.Restrict);
        t.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.DestinationWalletId).OnDelete(DeleteBehavior.Restrict);

        var m = modelBuilder.Entity<Movement>();
        m.ToTable("Movements", table =>
        {
            table.HasCheckConstraint("CK_Movements_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Movements_BalanceAfter", "[BalanceAfter] >= 0");
            table.HasCheckConstraint("CK_Movements_Source",
                "([PaymentId] IS NULL AND [DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit') OR " +
                "([PaymentId] IS NULL AND [DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = 'Transfer' AND [Type] IN ('Debit', 'Credit')) OR " +
                "([PaymentId] IS NOT NULL AND [DevelopmentFundingId] IS NULL AND [TransferId] IS NULL AND [Source] = 'Payment' AND [Type] = 'Credit')");
        });
        m.HasKey(x => x.Id);
        m.Property(x => x.Id).ValueGeneratedNever();
        m.Property(x => x.Amount).HasPrecision(18, 2);
        m.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        m.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        m.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        m.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<DevelopmentFunding>().WithOne().HasForeignKey<Movement>(x => x.DevelopmentFundingId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
        m.HasIndex(x => new { x.TransferId, x.WalletId, x.Type }).IsUnique().HasFilter("[TransferId] IS NOT NULL");
        m.HasIndex(x => new { x.WalletId, x.CreatedAt, x.Id });

        var p = modelBuilder.Entity<Payment>();
        p.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Payments_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
            table.HasCheckConstraint("CK_Payments_Status", "[Status] IN ('Created','Pending','Unknown','Approved','Rejected','Cancelled','Error')");
            table.HasCheckConstraint("CK_Payments_Credit", "([Status] = 'Approved' AND [CreditedAt] IS NOT NULL AND [ProviderPaymentId] IS NOT NULL) OR ([Status] <> 'Approved' AND [CreditedAt] IS NULL)");
        });
        p.HasKey(x => x.Id); p.Property(x => x.Id).ValueGeneratedNever();
        p.Property(x => x.Amount).HasPrecision(18, 2);
        p.Property(x => x.Currency).HasColumnType("char(3)").IsRequired();
        p.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        p.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        p.Property(x => x.RequestHash).HasColumnType("char(64)").IsRequired();
        p.Property(x => x.PreferenceId).HasMaxLength(128);
        p.Property(x => x.ProviderPaymentId).HasMaxLength(64);
        p.Property(x => x.CheckoutUrl).HasMaxLength(2048);
        p.Property(x => x.LastIssue).HasMaxLength(64);
        p.Property(x => x.RowVersion).IsRowVersion();
        p.HasIndex(x => new { x.WalletId, x.IdempotencyKey }).IsUnique();
        p.HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL");
        p.HasIndex(x => x.PreferenceId).IsUnique().HasFilter("[PreferenceId] IS NOT NULL");
        p.HasIndex(x => new { x.Status, x.NextCheckAt });
        p.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<Payment>().WithOne().HasForeignKey<Movement>(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);

        var receipt = modelBuilder.Entity<PaymentReceipt>();
        receipt.ToTable("PaymentReceipts"); receipt.HasKey(x => x.Id);
        receipt.Property(x => x.Id).ValueGeneratedNever();
        receipt.Property(x => x.EventKey).HasColumnType("char(64)").IsRequired();
        receipt.HasIndex(x => x.EventKey).IsUnique();
        receipt.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);

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
