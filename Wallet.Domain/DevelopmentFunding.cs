namespace Wallet.Domain;

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
