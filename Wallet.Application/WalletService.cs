using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public interface IWalletRepository
{
    Task<DomainWallet?> GetByUserIdAsync(Guid userId, CancellationToken ct);

    // La unicidad se resuelve en persistencia, también ante solicitudes concurrentes.
    Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct);
}

public sealed record WalletCreation(DomainWallet Wallet, bool Created);
public sealed record WalletResponse(Guid Id, decimal Balance, string Currency, DateTimeOffset CreatedAt);
public sealed record CreateWalletResult(WalletResponse Wallet, bool Created);

public sealed class WalletService(IWalletRepository wallets, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<WalletResponse?> GetMineAsync(CancellationToken ct)
    {
        var wallet = await wallets.GetByUserIdAsync(currentUser.Id, ct);
        return wallet is null ? null : Map(wallet);
    }

    private static WalletResponse Map(DomainWallet wallet) =>
        new(wallet.Id, wallet.Balance, wallet.Currency, wallet.CreatedAt);

    public async Task<CreateWalletResult> CreateAsync(CancellationToken ct)
    {
        var result = await wallets.CreateOrGetAsync(DomainWallet.Create(currentUser.Id, clock.GetUtcNow()), ct);
        return new(Map(result.Wallet), result.Created);
    }
}
