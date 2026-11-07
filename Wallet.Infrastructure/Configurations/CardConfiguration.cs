using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.CardHolderName).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Brand).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.CardNumberLast4).HasMaxLength(4).IsRequired();
        builder.Property(c => c.ExpirationMonth).IsRequired();
        builder.Property(c => c.ExpirationYear).IsRequired();
        builder.Property(c => c.CardToken).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CardToken).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
