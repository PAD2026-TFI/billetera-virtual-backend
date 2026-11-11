using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Application;

namespace Wallet.Infrastructure.Configurations;

public sealed class PasswordResetChallengeConfiguration : IEntityTypeConfiguration<PasswordResetChallenge>
{
    public void Configure(EntityTypeBuilder<PasswordResetChallenge> builder)
    {
        builder.ToTable("PasswordResetChallenges");
        builder.HasKey(challenge => challenge.Email);
        builder.Property(challenge => challenge.Email).HasMaxLength(254);
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(64).IsUnicode(false);
    }
}
