using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wallet.Domain;

namespace Wallet.Application;

public sealed class TransferRequest
{
    [Required] public Guid? DestinationWalletId { get; init; }
    [Required] public decimal? Amount { get; init; }
}
public sealed record TransferCommand(Guid SourceUserId, Guid DestinationWalletId, Money Amount, Guid IdempotencyKey)
{
    public TransferCommand(Guid sourceUserId, Guid destinationWalletId, decimal amount, Guid idempotencyKey)
        : this(sourceUserId, destinationWalletId, Money.Positive(amount), idempotencyKey) { }
}
public sealed record TransferResponse(Guid Id, Guid SourceWalletId, Guid DestinationWalletId,
    decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt)
{
    public static TransferResponse From(Transfer t) =>
        new(t.Id, t.SourceWalletId, t.DestinationWalletId, t.Amount.Amount, t.Amount.Currency, t.Status.ToString(), t.CreatedAt);
}
public sealed record TransferExecution(TransferResponse Transfer, bool Created);
public sealed class TransferRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public interface ITransferStore
{
    Task<TransferExecution> ExecuteAsync(TransferCommand command, string hash, CancellationToken ct);
}
public sealed class TransferService(ITransferStore store, ICurrentUser currentUser)
{
    public Task<TransferExecution> ExecuteAsync(TransferRequest request, string? key, CancellationToken ct)
    {
        if (request.DestinationWalletId is not Guid destination || destination == Guid.Empty || request.Amount is not decimal amount
            || !Guid.TryParse(key, out var parsedKey) || parsedKey == Guid.Empty)
            throw new TransferRejectedException("INVALID_INPUT");
        Money monetaryAmount;
        try { 
            monetaryAmount = Money.Positive(amount);
            Transfer.ValidateAmount(monetaryAmount);
        } catch (ArgumentException) { throw new TransferRejectedException("INVALID_AMOUNT"); }
        var command = new TransferCommand(currentUser.Id, destination, monetaryAmount, parsedKey);
        return store.ExecuteAsync(command, Fingerprint(destination, amount), ct);
    }
    public static string Fingerprint(Guid destination, decimal amount) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{destination:N}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS")));
}
