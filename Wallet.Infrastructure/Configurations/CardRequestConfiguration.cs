using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations
{
    internal class CardRequestConfiguration : IEntityTypeConfiguration<CardRequest>
    {
        public void Configure(EntityTypeBuilder<CardRequest> builder)
        {
            builder.ToTable("CardRequests");
            builder.HasKey(cr => cr);
            builder.Property(cr => cr.UserId).IsRequired();
            builder.Property(cr => cr.Status).IsRequired();
            builder.Property(cr => cr.CreatedAt).IsRequired();
            builder.Property(cr => cr.UpdatedAt).IsRequired();
        }
    }
    {
    }
}
