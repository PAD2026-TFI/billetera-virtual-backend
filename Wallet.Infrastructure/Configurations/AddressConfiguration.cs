using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Street).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Number).IsRequired().HasMaxLength(20);
        builder.Property(a => a.City).IsRequired().HasMaxLength(100);
        builder.Property(a => a.State).IsRequired().HasMaxLength(100);
        builder.Property(a => a.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(a => a.Country).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Latitude).HasPrecision(18, 6);
        builder.Property(a => a.Longitude).HasPrecision(18, 6);
        builder.Property(a => a.ValidationStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ExternalPlaceId).HasMaxLength(255);
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.ValidatedAt);
    }
}
