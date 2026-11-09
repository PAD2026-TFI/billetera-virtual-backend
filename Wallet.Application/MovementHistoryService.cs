using Wallet.Domain;

namespace Wallet.Application;

public sealed record MovementPosition(DateTimeOffset CreatedAt, Guid Id);
public interface IMovementCursorCodec
{
    string Encode(Guid walletId, MovementPosition position);
    MovementPosition? Decode(Guid walletId, string cursor);
}
public interface IMovementHistoryReader
{
    Task<IReadOnlyList<Movement>> ReadAsync(Guid walletId, int count, MovementPosition? before, CancellationToken ct);
}
public sealed record CounterpartyInfo(string Name, string Alias, string? Concept = null, bool IsPayment = false);
public interface ITransferCounterpartyResolver
{
    Task<IReadOnlyDictionary<Guid, CounterpartyInfo>> ResolveCounterpartiesAsync(
        Guid currentWalletId,
        IEnumerable<Guid> transferIds,
        IEnumerable<Guid> fundingIds,
        CancellationToken ct);
}
public sealed record MovementResponse(
    Guid Id,
    string Type,
    decimal Amount,
    string Currency,
    decimal BalanceAfter,
    string Source,
    Guid SourceId,
    DateTimeOffset CreatedAt,
    string? CounterpartyName = null,
    string? CounterpartyAlias = null,
    string? Concept = null,
    bool IsPayment = false);
public sealed record MovementPage(IReadOnlyList<MovementResponse> Items, string? NextCursor);

public sealed class MovementHistoryService(
    IWalletRepository wallets,
    IMovementHistoryReader history,
    IMovementCursorCodec cursors,
    ICurrentUser user,
    ITransferCounterpartyResolver? counterparties = null)
{
    public async Task<Result<MovementPage>> GetAsync(int limit, string? cursor, CancellationToken ct)
    {
        if (limit is < 1 or > 100 || cursor?.Length > 2048)
            return Result<MovementPage>.Failure("INVALID_INPUT", "Paginación inválida.");
        var wallet = await wallets.GetByUserIdAsync(user.Id, ct);
        if (wallet is null) return Result<MovementPage>.Failure("WALLET_NOT_FOUND", "No tenés una billetera creada.");
        var before = cursor is null ? null : cursors.Decode(wallet.Id, cursor);
        if (cursor is not null && before is null)
            return Result<MovementPage>.Failure("INVALID_INPUT", "Cursor inválido para esta billetera.");
        var rows = await history.ReadAsync(wallet.Id, limit + 1, before, ct);

        IReadOnlyDictionary<Guid, CounterpartyInfo>? counterpartyMap = null;
        if (counterparties is not null)
        {
            var transferIds = rows.Take(limit)
                .Where(m => m.Source == MovementSource.Transfer && m.TransferId.HasValue)
                .Select(m => m.TransferId!.Value);
            var fundingIds = rows.Take(limit)
                .Where(m => m.Source == MovementSource.DevelopmentFunding && m.DevelopmentFundingId.HasValue)
                .Select(m => m.DevelopmentFundingId!.Value);
            counterpartyMap = await counterparties.ResolveCounterpartiesAsync(wallet.Id, transferIds, fundingIds, ct);
        }

        var items = rows.Take(limit).Select(m =>
        {
            var sourceId = m.TransferId ?? m.DevelopmentFundingId
                ?? throw new InvalidOperationException("Movimiento sin referencia.");
            CounterpartyInfo? cp = null;
            counterpartyMap?.TryGetValue(sourceId, out cp);
            return new MovementResponse(
                m.Id,
                m.Type.ToString(),
                m.Amount.Amount,
                m.Amount.Currency,
                m.BalanceAfter.Amount,
                m.Source.ToString(),
                sourceId,
                m.CreatedAt,
                cp?.Name,
                cp?.Alias,
                cp?.Concept,
                cp?.IsPayment ?? false
            );
        }).ToArray();
        var next = rows.Count > limit
            ? cursors.Encode(wallet.Id, new(items[^1].CreatedAt, items[^1].Id)) : null;
        return Result<MovementPage>.Success(new(items, next));
    }
}
