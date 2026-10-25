using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public interface IWalletRepository
{
    // La unicidad se resuelve en persistencia, también ante solicitudes concurrentes.
    Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct);
}

public sealed record WalletCreation(DomainWallet Wallet, bool Created);
public sealed record WalletResponse(Guid Id, decimal Balance, string Currency, DateTimeOffset CreatedAt);
public sealed record CreateWalletResult(WalletResponse Wallet, bool Created);

public sealed class WalletService(IWalletRepository wallets, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<CreateWalletResult> CreateAsync(CancellationToken ct)
    {
        var result = await wallets.CreateOrGetAsync(DomainWallet.Create(currentUser.Id, clock.GetUtcNow()), ct);
        var wallet = result.Wallet;
        return new(new(wallet.Id, wallet.Balance, wallet.Currency, wallet.CreatedAt), result.Created);
    }
}
