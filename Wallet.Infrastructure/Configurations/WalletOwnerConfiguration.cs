using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class WalletOwnerConfiguration : IEntityTypeConfiguration<WalletOwner>
{
    public void Configure(EntityTypeBuilder<WalletOwner> builder)
    {
        builder.UseTptMappingStrategy();
        builder.ToTable("WalletOwners");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.RowVersion).IsRowVersion();
    }
}
