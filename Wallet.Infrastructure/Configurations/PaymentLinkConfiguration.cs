using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class PaymentLinkConfiguration : IEntityTypeConfiguration<PaymentLink>
{
    public void Configure(EntityTypeBuilder<PaymentLink> builder)
    {
        builder.ToTable("PaymentLinks", table =>
        {
            table.HasCheckConstraint("CK_PaymentLinks_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_PaymentLinks_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Title).HasMaxLength(128).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(512);

        builder.Property(l => l.Amount)
            .HasConversion(m => m.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);

        builder.Property(l => l.Currency)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .HasDefaultValue(Money.DefaultCurrency);

        builder.Property(l => l.Token)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(l => l.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(l => l.Type)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(l => l.CreatedAt).IsRequired();
        builder.Property(l => l.ExpiresAt);

        builder.HasIndex(l => l.Token).IsUnique();
        builder.HasIndex(l => l.DestinationWalletId);
        builder.HasIndex(l => l.MerchantOwnerId);

        builder.HasOne<global::Wallet.Domain.Wallet>()
            .WithMany()
            .HasForeignKey(l => l.DestinationWalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WalletOwner>()
            .WithMany()
            .HasForeignKey(l => l.MerchantOwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
