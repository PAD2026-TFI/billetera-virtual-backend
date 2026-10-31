using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class MovementConfiguration : IEntityTypeConfiguration<Movement>
{
    public void Configure(EntityTypeBuilder<Movement> builder) 
    {
        builder.ToTable("Movements", table =>
        {
            table.HasCheckConstraint("CK_Movements_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Movements_BalanceAfter", "[BalanceAfter] >= 0");
            table.HasCheckConstraint("CK_Movements_Source",
                "([DevelopmentFundingId] IS NOT NULL AND [TransferId] IS NULL AND [Source] = 'DevelopmentFunding' AND [Type] = 'Credit') OR " +
                "([DevelopmentFundingId] IS NULL AND [TransferId] IS NOT NULL AND [Source] = 'Transfer' AND [Type] IN ('Debit', 'Credit'))");
        });
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Amount)
            .HasConversion(m => m.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);
        builder.Property(movement => movement.BalanceAfter)
            .HasConversion(m => m.Amount, a => Money.From(a, Money.DefaultCurrency))
            .HasPrecision(18, 2);
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(movement => movement.Source).HasConversion<string>().HasMaxLength(32);
        builder.HasOne<global::Wallet.Domain.Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DevelopmentFunding>().WithOne().HasForeignKey<Movement>(x => x.DevelopmentFundingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(movement => new { movement.TransferId, movement.WalletId, movement.Type }).IsUnique().HasFilter("[TransferId] IS NOT NULL");
        builder.HasIndex(movement => new { movement.WalletId, movement.CreatedAt, movement.Id });
    }
}
