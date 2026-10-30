using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed record DevelopmentFundingRequest(Guid WalletId, decimal Amount, Guid IdempotencyKey);
public sealed record DevelopmentFundingResult(Guid Id, Guid WalletId, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, decimal BalanceAfter, bool Created);

public sealed class FundingRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface IDevelopmentFundingStore
{
    Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, string hash, CancellationToken ct);
}

public sealed class DevelopmentFundingService(IDevelopmentFundingStore store)
{
    public Task<DevelopmentFundingResult> FundAsync(DevelopmentFundingRequest request, CancellationToken ct)
    {
        MoneyRules.ValidatePositive(request.Amount);
        if (request.WalletId == Guid.Empty || request.IdempotencyKey == Guid.Empty)
            throw new ArgumentException("Identificador inválido.");
        return store.FundAsync(request, Fingerprint(request), ct);
    }

    public static string Fingerprint(DevelopmentFundingRequest request)
    {
        var canonical = $"{request.WalletId:N}|{request.Amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
