namespace Wallet.Domain;

public sealed class Wallet
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = "ARS";
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Wallet() { } // Materialización EF Core.

    public void Credit(decimal amount)
    {
        MoneyRules.ValidatePositive(amount);
        if (amount > MoneyRules.MaxBalance - Balance) throw new ArgumentException("El saldo excedería su capacidad.", nameof(amount));
        Balance += amount;
    }

    public static Wallet Create(Guid userId, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Titular inválido.", nameof(userId));
        return new Wallet
        {
            Id = Guid.NewGuid(), UserId = userId, Balance = 0m,
            Currency = "ARS", CreatedAt = now.ToUniversalTime()
        };
    }
}
