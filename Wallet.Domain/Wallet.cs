namespace Wallet.Domain;

/// <summary>
/// Representa la cuenta o billetera virtual de un titular en la plataforma.
/// Contiene saldo, CVU bancario/virtual de 22 dígitos, Alias de transferencia y control de concurrencia optimista.
/// </summary>
public sealed class Wallet
{
    /// <summary>
    /// Identificador único de la billetera.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador del titular de la billetera (Usuario o Comercio B2B).
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Alias de propiedad para compatibilidad con código existente que esperaba UserId.
    /// </summary>
    public Guid UserId => OwnerId;

    /// <summary>
    /// Clave Virtual Uniforme (CVU) de 22 dígitos numéricos con prefijo 00001234.
    /// </summary>
    public string Cvu { get; private set; } = string.Empty;

    /// <summary>
    /// Alias alfanumérico único para transferencias dentro y fuera de la red.
    /// </summary>
    public string Alias { get; private set; } = string.Empty;

    /// <summary>
    /// Saldo monetario actual disponible en la billetera.
    /// </summary>
    public Money Balance { get; private set; }

    /// <summary>
    /// Moneda operativa de la billetera (por defecto ARS).
    /// </summary>
    public string Currency { get; private set; } = Money.DefaultCurrency;

    /// <summary>
    /// Fecha y hora UTC en la que fue creada la billetera.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Marca de versión para el control de concurrencia optimista en Entity Framework Core.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    private Wallet() { } // Requerido por Entity Framework Core

    private Wallet(
        Guid id,
        Guid ownerId,
        string cvu,
        string alias,
        Money initialBalance,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Wallet identifier cannot be empty.", nameof(id));

        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner identifier cannot be empty.", nameof(ownerId));

        Id = id;
        OwnerId = ownerId;
        Cvu = ValidateCvu(cvu);
        Alias = ValidateAlias(alias);
        Balance = initialBalance;
        Currency = initialBalance.Currency;
        CreatedAt = createdAt.ToUniversalTime();
    }

    /// <summary>
    /// Crea una nueva billetera virtual vinculada a un titular con CVU y Alias generados externamente por Application.
    /// </summary>
    public static Wallet CreateForUser(
        Guid ownerId,
        string cvu,
        string initialAlias,
        DateTimeOffset now,
        string currency = Money.DefaultCurrency)
    {
        return new Wallet(Guid.NewGuid(), ownerId, cvu, initialAlias, Money.Zero(currency), now);
    }

    /// <summary>
    /// Compatibilidad hacia atrás: crea una billetera asignando un CVU y Alias predeterminado si no se especifican.
    /// </summary>
    public static Wallet Create(Guid ownerId, DateTimeOffset now, string currency = Money.DefaultCurrency)
    {
        var dummySuffix = Math.Abs(ownerId.GetHashCode() % 100000000000000L).ToString("D14");
        var cvu = $"00001234{dummySuffix}";
        var alias = $"wallet.{ownerId.ToString()[..8]}.ars";
        return new Wallet(Guid.NewGuid(), ownerId, cvu, alias, Money.Zero(currency), now);
    }

    /// <summary>
    /// Modifica el alias de la billetera validando longitud y formato alfanumérico.
    /// </summary>
    public void ModifyAlias(string newAlias)
    {
        Alias = ValidateAlias(newAlias);
    }

    /// <summary>
    /// Acredita fondos al saldo de la billetera.
    /// </summary>
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

    /// <summary>
    /// Sobrecarga para acreditar un monto decimal en la moneda de la billetera.
    /// </summary>
    public void Credit(decimal amount) => Credit(Money.Positive(amount, Balance.Currency));

    /// <summary>
    /// Debita fondos del saldo de la billetera comprobando disponibilidad de saldo.
    /// </summary>
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

    /// <summary>
    /// Sobrecarga para debitar un monto decimal en la moneda de la billetera.
    /// </summary>
    public void Debit(decimal amount) => Debit(Money.Positive(amount, Balance.Currency));

    private static string ValidateCvu(string cvu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cvu);
        var trimmed = cvu.Trim();
        if (trimmed.Length != 22 || !trimmed.All(char.IsDigit))
            throw new ArgumentException("CVU must consist of exactly 22 numeric digits.", nameof(cvu));

        if (!trimmed.StartsWith("00001234"))
            throw new ArgumentException("CVU must start with the system prefix '00001234'.", nameof(cvu));

        return trimmed;
    }

    private static string ValidateAlias(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        var trimmed = alias.Trim().ToLowerInvariant();
        if (trimmed.Length < 6 || trimmed.Length > 40)
            throw new ArgumentException("Alias must be between 6 and 40 characters.", nameof(alias));

        if (!trimmed.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-'))
            throw new ArgumentException("Alias can only contain letters, numbers, dots, and hyphens.", nameof(alias));

        return trimmed;
    }
}
