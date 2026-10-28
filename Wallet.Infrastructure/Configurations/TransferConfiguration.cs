using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

namespace Wallet.Infrastructure.Configurations
{
    public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
    {
        public void Configure(EntityTypeBuilder<Transfer> builder) 
        {
            builder.ToTable("Transfers", table =>
            {
                table.HasCheckConstraint("CK_Transfers_Amount", "[Amount] > 0 AND [Amount] <= 780400");
                table.HasCheckConstraint("CK_Transfers_Wallets", "[SourceWalletId] <> [DestinationWalletId]");
                table.HasCheckConstraint("CK_Transfers_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
                table.HasCheckConstraint("CK_Transfers_Status", "[Status] = 'Completed'");
            });
            builder.HasKey(transfer => transfer.Id); 
            builder.Property(transfer => transfer.Id).ValueGeneratedNever();
            builder.Property(transfer => transfer.Amount).HasPrecision(18, 2);
            builder.Property(transfer => transfer.Currency).HasColumnType("char(3)").HasMaxLength(3).IsRequired();
            builder.Property(transfer => transfer.Status).HasConversion<string>().HasMaxLength(16);
            builder.Property(transfer => transfer.RequestHash).HasColumnType("char(64)").HasMaxLength(64).IsRequired();
            builder.HasIndex(transfer => new { transfer.SourceWalletId, transfer.IdempotencyKey }).IsUnique();
            builder.HasOne<global::Wallet.Domain.Entities.Wallet>().WithMany().HasForeignKey(transfer => transfer.SourceWalletId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<global::Wallet.Domain.Entities.Wallet>().WithMany().HasForeignKey(transfer => transfer.DestinationWalletId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
