using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Domain;

namespace Wallet.Infrastructure.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder) 
        {
            builder.ToTable("Users");
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Id).ValueGeneratedNever();
            builder.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
            builder.Property(user => user.LastName).HasMaxLength(100).IsRequired();
            builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
            builder.Property(user => user.NormalizedEmail).HasMaxLength(254).UseCollation("Latin1_General_100_BIN2").IsRequired();
            builder.HasIndex(user => user.NormalizedEmail).IsUnique();
            builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            builder.Property(user => user.Role).HasMaxLength(20).IsRequired();
            builder.Property(user => user.RowVersion).IsRowVersion();
        }
    }
}
