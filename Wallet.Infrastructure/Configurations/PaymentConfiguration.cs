using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain.Entities;

namespace Wallet.Infrastructure.Configurations
{
    public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
    {
        public void Configure(EntityTypeBuilder<Payment> builder) 
        {
            builder.ToTable("Payments", table => 
            {
                table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            });
            builder.HasKey(p => p.Id);
            builder.Property(p => p.WalletId)
                .IsRequired();
            builder.Property(p => p.Amount)
                .IsRequired()
                .HasColumnType("decimal(18,2)");
            builder.Property(p => p.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(16);
            builder.Property(p => p.Currency)
                .IsRequired()
                .HasMaxLength(3);
            builder.Property(p => p.ExternalReference)
                .HasMaxLength(100);
            builder.Property(p => p.IdempotencyKey)
                .IsRequired();
            builder.Property(p => p.CreatedAt)
                .IsRequired();
            builder.Property(p => p.UpdatedAt)
                .IsRequired();
            builder.HasOne<global::Wallet.Domain.Entities.Wallet>().WithMany().HasForeignKey(payment => payment.WalletId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(builder => new { builder.WalletId, builder.IdempotencyKey }).IsUnique();
        }
    }
}
