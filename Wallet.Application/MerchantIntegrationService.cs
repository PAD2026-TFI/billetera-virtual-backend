using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Wallet.Domain;

namespace Wallet.Application;

public sealed record MerchantIntegrationRequest([Required, StringLength(100)] string Name);
public sealed record AccountTypeRequest([Required, RegularExpression("^(personal|merchant)$")] string AccountType);
public sealed record MerchantIntegrationResponse(
    string Status, string? Name, string? KeyPrefix, Guid? WalletId,
    DateTimeOffset? CreatedAt, DateTimeOffset? LastRotatedAt, DateTimeOffset? RevokedAt,
    string[] Scopes);
public sealed record MerchantIntegrationKeyResponse(MerchantIntegrationResponse Integration, string ApiKey);
public sealed class MerchantIntegrationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// There is exactly one credential per seller; it never creates another owner or wallet.
public sealed class MerchantIntegrationService(
    IUserRepository users, IWalletRepository wallets, ICurrentUser currentUser, TimeProvider clock)
{
    private async Task<User> GetUserAsync(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.Id, ct);
        if (user is null || !user.IsActive || user.Role != "Client")
            throw new MerchantIntegrationException("ACCESS_DENIED", "La cuenta no está activa.");
        return user;
    }

    private static MerchantIntegrationResponse Map(User user, Guid? walletId) => new(
        user.IntegrationKeyHash is not null ? "Active" : user.IntegrationCreatedAt.HasValue ? "Revoked" : "NotConfigured",
        user.IntegrationName, user.IntegrationKeyPrefix, walletId,
        user.IntegrationCreatedAt, user.IntegrationRotatedAt, user.IntegrationRevokedAt,
        ["payments:read", "payments:write"]);

    public async Task<MerchantIntegrationResponse> GetAsync(CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        var wallet = await wallets.GetByUserIdAsync(user.Id, ct);
        return Map(user, wallet?.Id);
    }

    public async Task<UserResponse> SetAccountTypeAsync(AccountTypeRequest request, CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        user.SetAccountType(request.AccountType);
        if (request.AccountType == "personal") user.RevokeIntegrationKey(clock.GetUtcNow());
        await users.SaveAsync(ct);
        return new UserResponse(user.Id, user.FirstName, user.LastName, user.Email, user.AccountType);
    }

    public async Task<MerchantIntegrationKeyResponse> IssueAsync(string? name, bool rotate, CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        if (user.AccountType != "merchant")
            throw new MerchantIntegrationException("MERCHANT_REQUIRED", "Activá tu perfil de vendedor antes de generar una clave.");
        var wallet = await wallets.GetByUserIdAsync(user.Id, ct);
        if (wallet is null)
            throw new MerchantIntegrationException("WALLET_UNAVAILABLE", "Necesitás una billetera activa para integrar cobros.");
        if (rotate ? user.IntegrationKeyHash is null : user.IntegrationKeyHash is not null)
            throw new MerchantIntegrationException("INTEGRATION_CONFLICT", rotate
                ? "No hay una clave activa para reemplazar. Generá una nueva."
                : "Ya tenés una clave activa. Usá Reemplazar o Revocar.");
        var key = "padi_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        user.SetIntegrationKey(rotate ? user.IntegrationName! : name!, GatewayService.HashApiKey(key), key[..13], clock.GetUtcNow());
        await users.SaveAsync(ct);
        return new MerchantIntegrationKeyResponse(Map(user, wallet.Id), key);
    }

    public async Task<MerchantIntegrationResponse> RevokeAsync(CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        user.RevokeIntegrationKey(clock.GetUtcNow());
        await users.SaveAsync(ct);
        var wallet = await wallets.GetByUserIdAsync(user.Id, ct);
        return Map(user, wallet?.Id);
    }
}
