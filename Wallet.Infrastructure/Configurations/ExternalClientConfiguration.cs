using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class ExternalClientConfiguration : IEntityTypeConfiguration<ExternalClient>
{
    public void Configure(EntityTypeBuilder<ExternalClient> builder)
    {
        builder.ToTable("ExternalClients");
        builder.Property(x => x.CompanyName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Cuit).HasMaxLength(11).IsRequired();
        builder.HasIndex(x => x.Cuit).IsUnique();
        builder.Property(x => x.ContactEmail).HasMaxLength(254).IsRequired();
        builder.Property(x => x.ApiKeyHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => x.ApiKeyHash).IsUnique().HasFilter("[ApiKeyHash] IS NOT NULL");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.WebhookUrl).HasMaxLength(2048);
        builder.Property(x => x.WebhookSecret).HasMaxLength(256);
        builder.Property(x => x.LastRotatedAt);
    }
}
