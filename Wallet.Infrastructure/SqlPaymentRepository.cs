using Microsoft.EntityFrameworkCore;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class SqlPaymentRepository(WalletDbContext db) : IPaymentRepository
{
    public async Task CreateAsync(Payment payment, CancellationToken ct)
    {
        await db.Payments.AddAsync(payment, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct)
    {
        db.Payments.Update(payment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Payment?> FindByIdempotencyKeyAsync(Guid merchantOwnerId, string idempotencyKey, CancellationToken ct)
    {
        return await db.Payments
            .FirstOrDefaultAsync(p => p.MerchantOwnerId == merchantOwnerId && p.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<IReadOnlyList<Payment>> GetByMerchantOwnerIdAsync(Guid merchantOwnerId, int limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 100);
        return await db.Payments
            .AsNoTracking()
            .Where(p => p.MerchantOwnerId == merchantOwnerId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Payment>> GetByPaymentLinkIdAsync(Guid paymentLinkId, CancellationToken ct)
    {
        return await db.Payments
            .AsNoTracking()
            .Where(p => p.PaymentLinkId == paymentLinkId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }
}
