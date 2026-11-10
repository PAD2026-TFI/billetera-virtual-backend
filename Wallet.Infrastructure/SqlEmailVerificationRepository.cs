using Microsoft.EntityFrameworkCore;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class SqlEmailVerificationRepository(WalletDbContext db) : IEmailVerificationRepository
{
    public Task<EmailVerificationChallenge?> FindForUpdateAsync(string email, CancellationToken ct) =>
        db.Set<EmailVerificationChallenge>()
            .FromSqlInterpolated($"SELECT * FROM [EmailVerificationChallenges] WITH (UPDLOCK, HOLDLOCK) WHERE [Email] = {email}")
            .SingleOrDefaultAsync(ct);

    public void Add(EmailVerificationChallenge challenge) => db.Add(challenge);

    public void Remove(EmailVerificationChallenge challenge) => db.Remove(challenge);
}
