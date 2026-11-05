using Wallet.Domain;

namespace Wallet.Application;

public interface IPaymentRepository
{
    Task CreateAsync(Payment payment, CancellationToken ct);
    Task UpdateAsync(Payment payment, CancellationToken ct);
    Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<Payment?> FindByIdempotencyKeyAsync(Guid merchantOwnerId, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetByMerchantOwnerIdAsync(Guid merchantOwnerId, int limit, CancellationToken ct);
    Task<IReadOnlyList<Payment>> GetByPaymentLinkIdAsync(Guid paymentLinkId, CancellationToken ct);
}
