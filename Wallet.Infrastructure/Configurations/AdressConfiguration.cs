using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;
namespace Wallet.Infrastructure.Configurations
{
    public class AdressConfiguration : IEntityTypeConfiguration<Adress>
    {
        public void Configure(EntityTypeBuilder<Adress> builder) 
        {
            builder.ToTable("Adresses");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Street).IsRequired().HasMaxLength(100);
            builder.Property(a => a.Number).IsRequired().HasMaxLength(10);
            builder.Property(a => a.City).IsRequired().HasMaxLength(50);
            builder.Property(a => a.State).IsRequired().HasMaxLength(50);
            builder.Property(a => a.PostalCode).IsRequired().HasMaxLength(20);
            builder.Property(a => a.Country).IsRequired().HasMaxLength(50);
            builder.Property(a => a.Latitude).IsRequired();
            builder.Property(a => a.Longitude).IsRequired();
            builder.Property(a => a.Status).IsRequired();
            builder.Property(a => a.UserId).IsRequired();
            builder.Property(a => a.CreatedAt).IsRequired();
            builder.Property(a => a.UpdatedAt).IsRequired();
        }
    }
}
