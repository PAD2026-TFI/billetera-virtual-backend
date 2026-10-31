using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class DevelopmentFundingConfiguration : IEntityTypeConfiguration<DevelopmentFunding>
{
    public void Configure(EntityTypeBuilder<DevelopmentFunding> builder)
    {
        builder.ToTable("DevelopmentFundings", table =>
        {
            table.HasCheckConstraint("CK_DevelopmentFundings_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_DevelopmentFundings_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount)
            .HasConversion(m => m.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasColumnType("char(3)").HasMaxLength(3).HasDefaultValue(Money.DefaultCurrency);
        builder.Property(x => x.RequestHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
    }
}
