using Wallet.Domain;

namespace Wallet.Application;

public interface IPaymentLinkRepository
{
    Task<PaymentLink?> FindByTokenAsync(string token, CancellationToken ct);
    Task<PaymentLink?> FindByIdAsync(Guid id, CancellationToken ct);
    Task CreateAsync(PaymentLink link, CancellationToken ct);
    Task UpdateAsync(PaymentLink link, CancellationToken ct);
    Task<IReadOnlyList<PaymentLink>> ListByMerchantAsync(Guid merchantOwnerId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentLink>>([]);
}
