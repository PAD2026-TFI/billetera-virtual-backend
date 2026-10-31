namespace Wallet.Domain;

public sealed class Wallet
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid UserId => OwnerId;
    public Money Balance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public string Currency { get; private set; } = Money.DefaultCurrency;

    private Wallet() { } // Required for EF Core materialization

    private Wallet(Guid id, Guid ownerId, Money initialBalance, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Wallet identifier cannot be empty.", nameof(id));

        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner identifier cannot be empty.", nameof(ownerId));

        Id = id;
        OwnerId = ownerId;
        Balance = initialBalance;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public static Wallet Create(Guid ownerId, DateTimeOffset now, string currency = Money.DefaultCurrency)
    {
        return new Wallet(Guid.NewGuid(), ownerId, Money.Zero(currency), now);
    }

    public void Credit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentException("Credit amount must be strictly greater than zero.", nameof(amount));

        if (amount.Currency != Balance.Currency)
            throw new InvalidOperationException($"Currency mismatch: cannot credit {amount.Currency} into a {Balance.Currency} wallet.");

        if (Balance.Amount + amount.Amount > Money.MaxAllowedAmount)
            throw new ArgumentException("Balance cannot exceed operational limit.", nameof(amount));

        Balance += amount;
    }

    public void Credit(decimal amount) => Credit(Money.Positive(amount, Balance.Currency));

    public void Debit(Money amount)
    {
        if (amount.Amount <= 0)
            throw new ArgumentException("Debit amount must be strictly greater than zero.", nameof(amount));

        if (amount.Currency != Balance.Currency)
            throw new InvalidOperationException($"Currency mismatch: cannot debit {amount.Currency} from a {Balance.Currency} wallet.");

        if (amount > Balance)
            throw new InvalidOperationException("Insufficient funds for debit operation.");

        Balance -= amount;
    }

    public void Debit(decimal amount) => Debit(Money.Positive(amount, Balance.Currency));
}