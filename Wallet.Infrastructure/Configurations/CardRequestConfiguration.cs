using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class CardRequestConfiguration : IEntityTypeConfiguration<CardRequest>
{
    public void Configure(EntityTypeBuilder<CardRequest> builder)
    {
        builder.ToTable("CardRequests");
        builder.HasKey(cr => cr.Id);
        builder.Property(cr => cr.Id).ValueGeneratedNever();
        builder.Property(cr => cr.UserId).IsRequired();
        builder.Property(cr => cr.AddressId).IsRequired();
        builder.Property(cr => cr.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(cr => cr.RequestedAt).IsRequired();
        builder.Property(cr => cr.UpdatedAt).IsRequired();
        builder.HasOne<Address>().WithMany().HasForeignKey(cr => cr.AddressId).OnDelete(DeleteBehavior.Restrict);
    }
}
