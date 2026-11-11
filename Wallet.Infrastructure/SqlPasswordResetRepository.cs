using Microsoft.EntityFrameworkCore;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class SqlPasswordResetRepository(WalletDbContext db) : IPasswordResetRepository
{
    public Task<PasswordResetChallenge?> FindForUpdateAsync(string email, CancellationToken ct) =>
        db.Set<PasswordResetChallenge>()
            .FromSqlInterpolated($"SELECT * FROM [PasswordResetChallenges] WITH (UPDLOCK, HOLDLOCK) WHERE [Email] = {email}")
            .SingleOrDefaultAsync(ct);

    public void Add(PasswordResetChallenge challenge) => db.Add(challenge);

    public void Remove(PasswordResetChallenge challenge) => db.Remove(challenge);
}
