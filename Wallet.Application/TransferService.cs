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

public sealed class TransferByCvuRequest
{
    [Required, StringLength(22, MinimumLength = 22, ErrorMessage = "El CVU debe tener exactamente 22 dígitos.")]
    public string Cvu { get; init; } = string.Empty;

    [Required, Range(0.01, 780400.00, ErrorMessage = "El importe debe estar entre 0,01 y 780400 ARS.")]
    public decimal Amount { get; init; }
}

public sealed class TransferByAliasRequest
{
    [Required, StringLength(40, MinimumLength = 6, ErrorMessage = "El Alias debe tener entre 6 y 40 caracteres.")]
    public string Alias { get; init; } = string.Empty;

    [Required, Range(0.01, 780400.00, ErrorMessage = "El importe debe estar entre 0,01 y 780400 ARS.")]
    public decimal Amount { get; init; }
}

public sealed class TransferByWalletIdRequest
{
    [Required]
    public Guid DestinationWalletId { get; init; }

    [Required, Range(0.01, 780400.00, ErrorMessage = "El importe debe estar entre 0,01 y 780400 ARS.")]
    public decimal Amount { get; init; }
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
public sealed class TransferService(ITransferStore store, ICurrentUser currentUser, IWalletRepository? wallets = null)
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

    public async Task<TransferExecution> ExecuteByCvuAsync(TransferByCvuRequest request, string? key, CancellationToken ct)
    {
        if (wallets is null) throw new TransferRejectedException("DESTINATION_UNAVAILABLE");
        var target = await wallets.FindByCvuAsync(request.Cvu.Trim(), ct)
            ?? throw new TransferRejectedException("DESTINATION_UNAVAILABLE");
        return await ExecuteAsync(new TransferRequest { DestinationWalletId = target.Id, Amount = request.Amount }, key, ct);
    }

    public async Task<TransferExecution> ExecuteByAliasAsync(TransferByAliasRequest request, string? key, CancellationToken ct)
    {
        if (wallets is null) throw new TransferRejectedException("DESTINATION_UNAVAILABLE");
        var target = await wallets.FindByAliasAsync(request.Alias.Trim().ToLowerInvariant(), ct)
            ?? throw new TransferRejectedException("DESTINATION_UNAVAILABLE");
        return await ExecuteAsync(new TransferRequest { DestinationWalletId = target.Id, Amount = request.Amount }, key, ct);
    }

    public Task<TransferExecution> ExecuteByWalletIdAsync(TransferByWalletIdRequest request, string? key, CancellationToken ct)
    {
        return ExecuteAsync(new TransferRequest { DestinationWalletId = request.DestinationWalletId, Amount = request.Amount }, key, ct);
    }

    public static string Fingerprint(Guid destination, decimal amount) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{destination:N}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS")));
}
