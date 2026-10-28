namespace Wallet.Domain;

public static class MoneyRules
{
    public const decimal MaxBalance = 9999999999999999.99m;
    public static void ValidatePositive(decimal amount)
    {
        if (amount <= 0 || amount > MaxBalance || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Importe positivo de hasta dos decimales fuera de rango.", nameof(amount));
    }
}

public sealed class DevelopmentFunding
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "ARS";
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    private DevelopmentFunding() { }

    public static DevelopmentFunding Create(Guid walletId, decimal amount, Guid key, string requestHash, DateTimeOffset now)
    {
        MoneyRules.ValidatePositive(amount);
        if (walletId == Guid.Empty || key == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        if (requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit)) throw new ArgumentException("Huella inválida.");
        return new() { Id = Guid.NewGuid(), WalletId = walletId, Amount = amount,
            IdempotencyKey = key, RequestHash = requestHash, CreatedAt = now.ToUniversalTime() };
    }
}
