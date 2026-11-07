namespace Wallet.Domain;

/// <summary>
/// Clase base abstracta que representa a un titular de billetera en el sistema (Usuario o Cliente Externo).
/// Gestiona la identidad, el estado de activación y el control de concurrencia optimista.
/// </summary>
public abstract class WalletOwner
{
    /// <summary>
    /// Identificador único del titular.
    /// </summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Indica si el titular se encuentra activo para operar en la plataforma.
    /// </summary>
    public bool IsActive { get; protected set; } = true;

    /// <summary>
    /// Fecha y hora UTC en la que fue registrado el titular.
    /// </summary>
    public DateTimeOffset CreatedAt { get; protected set; }

    /// <summary>
    /// Marca de versión para el control de concurrencia optimista en Entity Framework Core.
    /// </summary>
    public byte[] RowVersion { get; protected set; } = [];

    protected WalletOwner() { }

    protected WalletOwner(Guid id, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Owner ID cannot be empty.", nameof(id));

        Id = id;
        CreatedAt = createdAt.ToUniversalTime();
        IsActive = true;
    }

    /// <summary>
    /// Desactiva al titular para operar transacciones en la plataforma.
    /// </summary>
    public virtual void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Compatibilidad hacia atrás para desactivación.
    /// </summary>
    public virtual void Disable() => Deactivate();

    /// <summary>
    /// Reactiva al titular para operar en la plataforma.
    /// </summary>
    public virtual void Activate()
    {
        IsActive = true;
    }
}
