namespace Wallet.Domain;

/// <summary>
/// Representa una solicitud de emisión de tarjeta (física o virtual) asociada a un usuario y dirección de entrega.
/// Administra el ciclo de vida de la tarjeta (Pending -> Processing -> Issued -> InTransit -> Delivered).
/// </summary>
public sealed class CardRequest
{
    /// <summary>
    /// Identificador único de la solicitud de emisión.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador del usuario solicitante.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Identificador de la dirección física validada donde se despachará la tarjeta.
    /// </summary>
    public Guid AddressId { get; private set; }

    /// <summary>
    /// Tipo de tarjeta solicitada (Débito o Crédito).
    /// </summary>
    public CardType Type { get; private set; }

    /// <summary>
    /// Estado actual en el ciclo de vida de emisión.
    /// </summary>
    public CardRequestStatus Status { get; private set; }

    /// <summary>
    /// Fecha y hora UTC en la que se inició la solicitud.
    /// </summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// Fecha y hora UTC del último cambio de estado.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    private CardRequest() { }

    private CardRequest(Guid id, Guid userId, Guid addressId, CardType type, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        AddressId = addressId;
        Type = type;
        Status = CardRequestStatus.Pending;
        RequestedAt = now.ToUniversalTime();
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// Método de fábrica para crear una solicitud de tarjeta vinculada a una dirección con estado validado.
    /// </summary>
    public static CardRequest Create(Guid userId, Address address, DateTimeOffset now, CardType type = CardType.Debit)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        ArgumentNullException.ThrowIfNull(address);

        if (address.ValidationStatus != AddressValidationStatus.Valid)
            throw new InvalidOperationException("Cannot request a card with an unvalidated address.");

        return new CardRequest(Guid.NewGuid(), userId, address.Id, type, now);
    }

    /// <summary>
    /// Actualiza el estado de la solicitud verificando las transiciones permitidas por la máquina de estados.
    /// </summary>
    public void UpdateStatus(CardRequestStatus newStatus, DateTimeOffset now)
    {
        var isValid = (Status, newStatus) switch
        {
            (CardRequestStatus.Pending, CardRequestStatus.Processing) => true,
            (CardRequestStatus.Pending, CardRequestStatus.Cancelled) => true,
            (CardRequestStatus.Pending, CardRequestStatus.Rejected) => true,
            (CardRequestStatus.Processing, CardRequestStatus.Issued) => true,
            (CardRequestStatus.Processing, CardRequestStatus.Cancelled) => true,
            (CardRequestStatus.Processing, CardRequestStatus.Rejected) => true,
            (CardRequestStatus.Issued, CardRequestStatus.InTransit) => true,
            (CardRequestStatus.Issued, CardRequestStatus.Cancelled) => true,
            (CardRequestStatus.InTransit, CardRequestStatus.Delivered) => true,
            _ => false
        };

        if (!isValid)
            throw new InvalidOperationException($"Invalid status transition from {Status} to {newStatus}.");

        Status = newStatus;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// Aprueba la solicitud y la pasa a estado en proceso de fabricación.
    /// </summary>
    public void Approve(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Processing, now);

    /// <summary>
    /// Pasa la solicitud al estado en proceso.
    /// </summary>
    public void Process(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Processing, now);

    /// <summary>
    /// Emite la tarjeta plástica o credencial digital.
    /// </summary>
    public void Issue(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Issued, now);

    /// <summary>
    /// Inicia el despacho y entrega de la tarjeta con la empresa de logística.
    /// </summary>
    public void StartDelivery(DateTimeOffset now) => UpdateStatus(CardRequestStatus.InTransit, now);

    /// <summary>
    /// Sinónimo en inglés para despachar la tarjeta.
    /// </summary>
    public void Ship(DateTimeOffset now) => UpdateStatus(CardRequestStatus.InTransit, now);

    /// <summary>
    /// Confirma la recepción exitosa por parte del usuario titular.
    /// </summary>
    public void MarkDelivered(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Delivered, now);

    /// <summary>
    /// Cancela la solicitud de tarjeta.
    /// </summary>
    public void Cancel(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Cancelled, now);

    /// <summary>
    /// Rechaza la solicitud por motivos regulatorios o crediticios.
    /// </summary>
    public void Reject(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Rejected, now);
}
