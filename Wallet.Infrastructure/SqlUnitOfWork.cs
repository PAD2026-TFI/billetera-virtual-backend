using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class SqlUnitOfWork : IUnitOfWork
{
    private readonly WalletDbContext _db;

    public SqlUnitOfWork(WalletDbContext db)
    {
        _db = db;
    }

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            return new NoOpTransactionScope();
        }

        try
        {
            var tx = await _db.Database.BeginTransactionAsync(ct);
            return new SqlTransactionScope(tx);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return new NoOpTransactionScope();
        }
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }

    private sealed class SqlTransactionScope : ITransactionScope
    {
        private readonly IDbContextTransaction _tx;

        public SqlTransactionScope(IDbContextTransaction tx)
        {
            _tx = tx;
        }

        public Task CommitAsync(CancellationToken ct) => _tx.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct) => _tx.RollbackAsync(ct);

        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }

    private sealed class NoOpTransactionScope : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
