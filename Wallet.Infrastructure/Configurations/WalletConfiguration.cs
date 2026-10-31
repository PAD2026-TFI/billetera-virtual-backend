using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Domain.Wallet>
{
    public void Configure(EntityTypeBuilder<Domain.Wallet> builder) 
    {
        builder.ToTable("Wallets", table =>
        {
            table.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0");
            table.HasCheckConstraint("CK_Wallets_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });
        builder.HasKey(wallet => wallet.Id);
        builder.Property(wallet => wallet.Id).ValueGeneratedNever();
        builder.Property(wallet => wallet.Balance)
            .HasConversion(b => b.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);
        builder.Ignore(wallet => wallet.UserId);
        builder.Property(wallet => wallet.Currency)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .HasDefaultValue(Money.DefaultCurrency);
        builder.Property(wallet => wallet.RowVersion).IsRowVersion();
        builder.Property(wallet => wallet.OwnerId).HasColumnName("OwnerId").IsRequired();
        builder.HasIndex(wallet => wallet.OwnerId).IsUnique();
        builder.HasOne<WalletOwner>().WithOne().HasForeignKey<Domain.Wallet>(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
