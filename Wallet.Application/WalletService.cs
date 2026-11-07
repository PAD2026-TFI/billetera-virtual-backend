using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Application;

public interface IWalletRepository
{
    Task<DomainWallet?> GetByUserIdAsync(Guid userId, CancellationToken ct);

    // La unicidad se resuelve en persistencia, también ante solicitudes concurrentes.
    Task<WalletCreation> CreateOrGetAsync(DomainWallet candidate, CancellationToken ct);

    Task<bool> ExistsCvuAsync(string cvu, CancellationToken ct) => Task.FromResult(false);

    Task<bool> ExistsAliasAsync(string alias, CancellationToken ct) => Task.FromResult(false);

    Task<DomainWallet?> FindByCvuAsync(string cvu, CancellationToken ct) => Task.FromResult<DomainWallet?>(null);

    Task<DomainWallet?> FindByAliasAsync(string alias, CancellationToken ct) => Task.FromResult<DomainWallet?>(null);

    Task<DomainWallet?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<DomainWallet?>(null);

    Task CreateAsync(DomainWallet wallet, CancellationToken ct) => Task.CompletedTask;

    Task UpdateAsync(DomainWallet wallet, CancellationToken ct) => Task.CompletedTask;
}

public sealed record WalletCreation(DomainWallet Wallet, bool Created);
public sealed record WalletResponse(
    Guid Id,
    decimal Balance,
    string Currency,
    DateTimeOffset CreatedAt,
    string Cvu = "",
    string Alias = "");
public sealed record CreateWalletResult(WalletResponse Wallet, bool Created);

public sealed record ModifyAliasRequest(string NewAlias);

public sealed class WalletService(IWalletRepository wallets, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<WalletResponse?> GetMineAsync(CancellationToken ct)
    {
        var wallet = await wallets.GetByUserIdAsync(currentUser.Id, ct);
        return wallet is null ? null : Map(wallet);
    }

    private static WalletResponse Map(DomainWallet wallet) =>
        new(wallet.Id, wallet.Balance.Amount, wallet.Currency, wallet.CreatedAt, wallet.Cvu, wallet.Alias);

    public async Task<CreateWalletResult> CreateAsync(CancellationToken ct)
    {
        var result = await wallets.CreateOrGetAsync(DomainWallet.Create(currentUser.Id, clock.GetUtcNow()), ct);
        return new CreateWalletResult(Map(result.Wallet), result.Created);
    }

    public async Task<WalletResponse> ModifyAliasAsync(string newAlias, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newAlias);
        var normalizedAlias = newAlias.Trim().ToLowerInvariant();

        var wallet = await wallets.GetByUserIdAsync(currentUser.Id, ct)
            ?? throw new KeyNotFoundException("Wallet was not found for current user.");

        if (await wallets.ExistsAliasAsync(normalizedAlias, ct))
            throw new InvalidOperationException("The requested alias is already in use.");

        wallet.ModifyAlias(normalizedAlias);
        await wallets.UpdateAsync(wallet, ct);
        return Map(wallet);
    }
}
