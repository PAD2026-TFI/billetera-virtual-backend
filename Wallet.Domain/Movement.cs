namespace Wallet.Domain;

public sealed class Movement
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public MovementType Type { get; private set; }
    public Money Amount { get; private set; }
    public Money BalanceAfter { get; private set; }
    public MovementSource Source { get; private set; }
    public Guid? TransferId { get; private set; }
    public Guid? DevelopmentFundingId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string Currency => Amount.Currency;

    private Movement() { } 

    private Movement(
        Guid id,
        Guid walletId,
        MovementType type,
        Money amount,
        Money balanceAfter,
        MovementSource source,
        Guid? transferId,
        Guid? developmentFundingId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Movement identifier cannot be empty.", nameof(id));

        if (walletId == Guid.Empty)
            throw new ArgumentException("Wallet identifier cannot be empty.", nameof(walletId));

        if (amount.Amount <= 0)
            throw new ArgumentException("Movement amount must be strictly greater than zero.", nameof(amount));

        if (amount.Currency != balanceAfter.Currency)
            throw new InvalidOperationException($"Currency mismatch between amount ({amount.Currency}) and balance after ({balanceAfter.Currency}).");

        ValidateSourceReferences(source, transferId, developmentFundingId);
        ValidateBalanceAfter(type, amount, balanceAfter);

        Id = id;
        WalletId = walletId;
        Type = type;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Source = source;
        TransferId = transferId;
        DevelopmentFundingId = developmentFundingId;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public static IReadOnlyList<Movement> ForCompletedTransfer(Transfer transfer, Wallet source, Wallet destination)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (transfer.Status != TransferStatus.Completed)
            throw new InvalidOperationException("Movements can only be registered for completed transfers.");

        if (source.Id != transfer.SourceWalletId || destination.Id != transfer.DestinationWalletId)
            throw new ArgumentException("Wallets do not correspond to the completed transfer boundaries.");

        if (source.Currency != transfer.Currency || destination.Currency != transfer.Currency)
            throw new InvalidOperationException("Wallet currency does not match transfer currency.");

        return Array.AsReadOnly([
            ForTransfer(transfer, isDebit: true, source.Balance),
            ForTransfer(transfer, isDebit: false, destination.Balance)
        ]);
    }

    public static Movement ForTransfer(Transfer transfer, bool isDebit, Money balanceAfter)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        var transferAmount = transfer.Amount;
        var targetWalletId = isDebit ? transfer.SourceWalletId : transfer.DestinationWalletId;
        var movementType = isDebit ? MovementType.Debit : MovementType.Credit;

        return new Movement(
            id: Guid.NewGuid(),
            walletId: targetWalletId,
            type: movementType,
            amount: transferAmount,
            balanceAfter: balanceAfter,
            source: MovementSource.Transfer,
            transferId: transfer.Id,
            developmentFundingId: null,
            createdAt: transfer.CreatedAt);
    }

    public static Movement ForTransfer(Transfer transfer, bool isDebit, decimal balanceAfter) =>
        ForTransfer(transfer, isDebit, Money.From(balanceAfter, transfer.Currency));

    public static Movement ForDevelopmentFunding(DevelopmentFunding funding, Money balanceAfter)
    {
        ArgumentNullException.ThrowIfNull(funding);

        var fundingAmount = funding.Amount;

        return new Movement(
            id: Guid.NewGuid(),
            walletId: funding.WalletId,
            type: MovementType.Credit,
            amount: fundingAmount,
            balanceAfter: balanceAfter,
            source: MovementSource.DevelopmentFunding,
            transferId: null,
            developmentFundingId: funding.Id,
            createdAt: funding.CreatedAt);
    }

    public static Movement ForDevelopmentFunding(DevelopmentFunding funding, decimal balanceAfter) =>
        ForDevelopmentFunding(funding, Money.From(balanceAfter, funding.Currency));

    private static void ValidateSourceReferences(MovementSource source, Guid? transferId, Guid? developmentFundingId)
    {
        var valid = source switch
        {
            MovementSource.Transfer => transferId.HasValue && !developmentFundingId.HasValue,
            MovementSource.DevelopmentFunding => developmentFundingId.HasValue && !transferId.HasValue,
            _ => false
        };

        if (!valid)
            throw new InvalidOperationException($"Invalid source references for movement source '{source}'. Exactly one matching identifier must be populated.");
    }

    private static void ValidateBalanceAfter(MovementType type, Money amount, Money balanceAfter)
    {
        if (type == MovementType.Credit && balanceAfter < amount)
            throw new ArgumentException($"Credit movement balance after ({balanceAfter.Amount}) cannot be less than credited amount ({amount.Amount}).", nameof(balanceAfter));
    }
}