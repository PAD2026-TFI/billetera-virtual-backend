using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Infrastructure.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Payments_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Amount)
            .HasConversion(m => m.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .HasDefaultValue(Money.DefaultCurrency);

        builder.Property(p => p.Method)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.IdempotencyKey).HasMaxLength(128);
        builder.Property(p => p.RequestHash).HasMaxLength(128);
        builder.Property(p => p.CardBrand).HasMaxLength(32);
        builder.Property(p => p.CardLast4).HasColumnType("char(4)").HasMaxLength(4);
        builder.Property(p => p.CardHolderName).HasMaxLength(128);
        builder.Property(p => p.AuthorizationCode).HasMaxLength(64);
        builder.Property(p => p.FailureReason).HasMaxLength(256);

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CompletedAt);

        builder.HasIndex(p => p.PaymentLinkId);
        builder.HasIndex(p => new { p.MerchantOwnerId, p.CreatedAt });
        builder.HasIndex(p => new { p.MerchantOwnerId, p.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        builder.HasOne<PaymentLink>()
            .WithMany()
            .HasForeignKey(p => p.PaymentLinkId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WalletOwner>()
            .WithMany()
            .HasForeignKey(p => p.MerchantOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DomainWallet>()
            .WithMany()
            .HasForeignKey(p => p.DestinationWalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DomainWallet>()
            .WithMany()
            .HasForeignKey(p => p.PayerWalletId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.PayerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(p => p.TransferId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
