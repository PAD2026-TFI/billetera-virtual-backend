using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlPaymentLinkRepository(WalletDbContext db) : IPaymentLinkRepository
{
    public async Task<PaymentLink?> FindByTokenAsync(string token, CancellationToken ct)
    {
        var normalized = token.Trim().ToLowerInvariant();
        return await db.PaymentLinks
            .FirstOrDefaultAsync(l => l.Token == normalized, ct);
    }

    public async Task<PaymentLink?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.PaymentLinks
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task CreateAsync(PaymentLink link, CancellationToken ct)
    {
        await db.PaymentLinks.AddAsync(link, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PaymentLink link, CancellationToken ct)
    {
        db.PaymentLinks.Update(link);
        await db.SaveChangesAsync(ct);
    }
}
