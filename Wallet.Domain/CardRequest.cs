namespace Wallet.Domain;

public sealed class CardRequest
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid AddressId { get; private set; }
    public CardType Type { get; private set; }
    public CardRequestStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
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

    public static CardRequest Create(Guid userId, Address address, DateTimeOffset now, CardType type = CardType.Debit)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        ArgumentNullException.ThrowIfNull(address);

        if (address.ValidationStatus != AddressValidationStatus.Valid)
            throw new InvalidOperationException("Cannot request a card with an unvalidated address.");

        return new CardRequest(Guid.NewGuid(), userId, address.Id, type, now);
    }

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

    public void Process(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Processing, now);

    public void Issue(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Issued, now);

    public void Ship(DateTimeOffset now) => UpdateStatus(CardRequestStatus.InTransit, now);

    public void MarkDelivered(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Delivered, now);

    public void Cancel(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Cancelled, now);

    public void Reject(DateTimeOffset now) => UpdateStatus(CardRequestStatus.Rejected, now);
}
