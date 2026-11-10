using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder) 
    {
        builder.ToTable("Users");
        builder.Property(user => user.Dni).HasMaxLength(20).IsRequired();
        builder.HasIndex(user => user.Dni).IsUnique().HasFilter("[Dni] <> '00000000'");
        builder.Property(user => user.IdentityVerifiedAt);
        builder.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(254).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.Role).HasMaxLength(20).IsRequired();
        builder.Property(user => user.SessionVersion).IsRequired();
        builder.Property(user => user.WalletId);
        builder.Property(user => user.AccountType).HasMaxLength(20).HasDefaultValue("personal").IsRequired();
        builder.Property(user => user.IntegrationVersion).HasDefaultValue(0).IsConcurrencyToken();
        builder.Property(user => user.IntegrationName).HasMaxLength(100);
        builder.Property(user => user.IntegrationKeyHash).HasMaxLength(64);
        builder.Property(user => user.IntegrationKeyPrefix).HasMaxLength(20);
        builder.HasIndex(user => user.IntegrationKeyHash).IsUnique().HasFilter("[IntegrationKeyHash] IS NOT NULL");
    }
}
