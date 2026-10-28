using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

namespace Wallet.Infrastructure.Configurations
{
    public class WalletConfiguration : IEntityTypeConfiguration<Domain.Entities.Wallet>
    {
        public void Configure(EntityTypeBuilder<Domain.Entities.Wallet> builder) 
        {
            builder.ToTable("Wallets", table =>
            {
                table.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0");
                table.HasCheckConstraint("CK_Wallets_Currency", "[Currency] COLLATE Latin1_General_100_BIN2 = 'ARS'");
            });
            builder.HasKey(wallet => wallet.Id);
            builder.Property(wallet => wallet.Id).ValueGeneratedNever();
            builder.Property(wallet => wallet.Balance).HasPrecision(18, 2);
            builder.Property(wallet => wallet.Currency).HasColumnType("char(3)").HasMaxLength(3).IsRequired();
            builder.Property(wallet => wallet.RowVersion).IsRowVersion();
            builder.HasIndex(wallet => wallet.UserId).IsUnique();
            builder.HasOne<User>().WithOne().HasForeignKey<global::Wallet.Domain.Entities.Wallet>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
