using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wallet.Application;

namespace Wallet.Infrastructure.Configurations;

public sealed class EmailVerificationChallengeConfiguration : IEntityTypeConfiguration<EmailVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<EmailVerificationChallenge> builder)
    {
        builder.ToTable("EmailVerificationChallenges");
        builder.HasKey(challenge => challenge.Email);
        builder.Property(challenge => challenge.Email).HasMaxLength(254);
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(challenge => challenge.ProofHash).HasMaxLength(64).IsUnicode(false);
    }
}
