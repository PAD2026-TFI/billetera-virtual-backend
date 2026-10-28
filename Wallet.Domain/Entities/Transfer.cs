using Wallet.Domain.Entities.Status;

namespace Wallet.Domain.Entities;

public sealed class Transfer
{
    public const decimal MaximumAmount = 780400m;
    public Guid Id { get; private set; }
    public Guid SourceWalletId { get; private set; }
    public Guid DestinationWalletId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "ARS";
    public TransferStatus Status { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    private Transfer() { }

    public bool MatchesRequest(Guid destination, decimal amount, string requestHash) =>
        Status == TransferStatus.Completed && Currency == "ARS" && DestinationWalletId == destination
        && Amount == amount && RequestHash == requestHash;

    public void ApplyTo(Wallet source, Wallet destination)
    {
        // Valido ambos extremos antes de tocar cualquiera de los dos saldos.
        if (Status != TransferStatus.Completed || source.Id != SourceWalletId || destination.Id != DestinationWalletId
            || source.Currency != Currency || destination.Currency != Currency)
            throw new ArgumentException("Las billeteras no corresponden a esta transferencia.");
        ValidateAmount(Amount);
        if (source.Balance < Amount) throw new ArgumentException("Saldo insuficiente.");
        if (Amount > MoneyRules.MaxBalance - destination.Balance) throw new ArgumentException("El destino excedería su capacidad.");
        source.Debit(Amount);
        destination.Credit(Amount);
    }

    public static void ValidateAmount(decimal amount)
    {
        MoneyRules.ValidatePositive(amount);
        if (amount > MaximumAmount) throw new ArgumentException("El importe supera el límite de transferencia.");
    }

    public static Transfer Complete(Guid source, Guid destination, decimal amount, Guid key, string hash, DateTimeOffset now)
    {
        ValidateAmount(amount);
        if (source == Guid.Empty || destination == Guid.Empty || key == Guid.Empty || source == destination)
            throw new ArgumentException("Identificadores de transferencia inválidos.");
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Huella inválida.");
        return new() { Id = Guid.NewGuid(), SourceWalletId = source, DestinationWalletId = destination,
            Amount = amount, Status = TransferStatus.Completed, IdempotencyKey = key, RequestHash = hash,
            CreatedAt = now.ToUniversalTime() };
    }
}
