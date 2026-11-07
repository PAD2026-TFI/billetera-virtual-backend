namespace Wallet.Application;

/// <summary>
/// Contrato de Unidad de Trabajo (Unit of Work) para orquestar transacciones atómicas de base de datos.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Inicia una transacción asíncrona de base de datos.
    /// </summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct);

    /// <summary>
    /// Guarda los cambios en el contexto de persistencia.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Representa el alcance de una transacción atómica con soporte para confirmación o reversión.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    /// <summary>
    /// Confirma la transacción persistiendo los cambios de forma irreversible.
    /// </summary>
    Task CommitAsync(CancellationToken ct);

    /// <summary>
    /// Revierte la transacción descartando todos los cambios realizados en el contexto.
    /// </summary>
    Task RollbackAsync(CancellationToken ct);
}
