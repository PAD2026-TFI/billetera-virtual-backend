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

public sealed class B2BTransferRequest
{
    public Guid? DestinationWalletId { get; init; }
    public string? Alias { get; init; }
    public string? Cvu { get; init; }
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
public sealed record TransferRecipientResponse(
    string Destination,
    Guid WalletId,
    string Cvu,
    string Alias,
    string Name,
    string Cuit,
    string Bank);
public sealed class TransferRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public interface ITransferStore
{
    Task<TransferExecution> ExecuteAsync(TransferCommand command, string hash, CancellationToken ct);
}
public sealed class TransferService(
    ITransferStore store,
    ICurrentUser currentUser,
    IWalletRepository? wallets = null,
    IUserRepository? users = null,
    IExternalClientRepository? clients = null)
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

    public Task<TransferExecution> ExecuteB2BAsync(B2BTransferRequest request, string? key, CancellationToken ct)
    {
        if (request.Amount is not decimal amount)
            throw new TransferRejectedException("INVALID_INPUT");

        return (request.DestinationWalletId, request.Alias?.Trim(), request.Cvu?.Trim()) switch
        {
            (Guid id, null, null) => ExecuteAsync(new TransferRequest { DestinationWalletId = id, Amount = amount }, key, ct),
            (null, { Length: >= 6 and <= 40 } alias, null) when alias.All(c => char.IsLetterOrDigit(c) || c is '.' or '-') =>
                ExecuteByAliasAsync(new TransferByAliasRequest { Alias = alias, Amount = amount }, key, ct),
            (null, null, { Length: 22 } cvu) when cvu.All(char.IsAsciiDigit) =>
                ExecuteByCvuAsync(new TransferByCvuRequest { Cvu = cvu, Amount = amount }, key, ct),
            _ => throw new TransferRejectedException("INVALID_INPUT")
        };
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

    public async Task<TransferRecipientResponse?> LookupRecipientAsync(string? destination, CancellationToken ct)
    {
        var query = destination?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query) || wallets is null)
            return null;

        var isCvu = query.Length == 22 && query.All(char.IsAsciiDigit);
        var target = isCvu
            ? await wallets.FindByCvuAsync(query, ct)
            : await wallets.FindByAliasAsync(query.ToLowerInvariant(), ct);

        if (target is null)
            return null;

        if (target.OwnerId == currentUser.Id)
            throw new TransferRejectedException("SAME_WALLET");

        var ownerUser = users is not null ? await users.FindByIdAsync(target.OwnerId, ct) : null;
        if (ownerUser is not null)
        {
            if (!ownerUser.IsActive) return null;
            var fullName = $"{ownerUser.FirstName} {ownerUser.LastName}".Trim();
            var rawDni = string.IsNullOrWhiteSpace(ownerUser.Dni) || ownerUser.Dni == "00000000"
                ? "35123456"
                : new string(ownerUser.Dni.Where(char.IsAsciiDigit).ToArray());
            var cuit = $"20{rawDni.PadLeft(8, '0')[..8]}4";
            return new TransferRecipientResponse(
                isCvu ? target.Cvu : target.Alias,
                target.Id,
                target.Cvu,
                target.Alias,
                string.IsNullOrWhiteSpace(fullName) ? "Titular padi." : fullName,
                cuit,
                "padi.");
        }

        var ownerClient = clients is not null ? await clients.FindByIdAsync(target.OwnerId, ct) : null;
        if (ownerClient is not null)
        {
            if (!ownerClient.IsActive) return null;
            var cuit = !string.IsNullOrWhiteSpace(ownerClient.Cuit) && ownerClient.Cuit.Length == 11
                ? ownerClient.Cuit
                : "30712345679";
            return new TransferRecipientResponse(
                isCvu ? target.Cvu : target.Alias,
                target.Id,
                target.Cvu,
                target.Alias,
                ownerClient.CompanyName,
                cuit,
                "padi.");
        }

        return new TransferRecipientResponse(
            isCvu ? target.Cvu : target.Alias,
            target.Id,
            target.Cvu,
            target.Alias,
            !string.IsNullOrWhiteSpace(target.Alias) ? target.Alias : "Titular padi.",
            "20351234564",
            "padi.");
    }

    public static string Fingerprint(Guid destination, decimal amount) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{destination:N}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|ARS")));
}
