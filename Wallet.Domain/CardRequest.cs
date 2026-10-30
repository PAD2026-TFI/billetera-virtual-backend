namespace Wallet.Domain;

public sealed class CardRequest
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid AddressId { get; private set; }
    public CardRequestStatus Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CardRequest() { }

    private CardRequest(Guid id, Guid userId, Guid addressId, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        AddressId = addressId;
        Status = CardRequestStatus.Pending;
        RequestedAt = now.ToUniversalTime();
        UpdatedAt = now.ToUniversalTime();
    }

    public static CardRequest Create(Guid userId, Address address, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));

        ArgumentNullException.ThrowIfNull(address);

        if (address.ValidationStatus != AddressValidationStatus.Valid)
            throw new InvalidOperationException("Cannot request a physical card with an unvalidated address.");

        return new CardRequest(Guid.NewGuid(), userId, address.Id, now);
    }

    public void Approve(DateTimeOffset now) => TransitionTo(CardRequestStatus.Approved, now);

    public void Reject(DateTimeOffset now) => TransitionTo(CardRequestStatus.Rejected, now);

    public void StartDelivery(DateTimeOffset now) => TransitionTo(CardRequestStatus.InDelivery, now);

    public void MarkDelivered(DateTimeOffset now) => TransitionTo(CardRequestStatus.Delivered, now);

    public void Cancel(DateTimeOffset now) => TransitionTo(CardRequestStatus.Cancelled, now);

    public void TransitionTo(CardRequestStatus nextStatus, DateTimeOffset now)
    {
        var isValid = (Status, nextStatus) switch
        {
            (CardRequestStatus.Pending, CardRequestStatus.Approved) => true,
            (CardRequestStatus.Pending, CardRequestStatus.Rejected) => true,
            (CardRequestStatus.Pending, CardRequestStatus.Cancelled) => true,
            (CardRequestStatus.Approved, CardRequestStatus.InDelivery) => true,
            (CardRequestStatus.Approved, CardRequestStatus.Cancelled) => true,
            (CardRequestStatus.InDelivery, CardRequestStatus.Delivered) => true,
            _ => false
        };

        if (!isValid)
            throw new InvalidOperationException($"Invalid status transition from {Status} to {nextStatus}.");

        Status = nextStatus;
        UpdatedAt = now.ToUniversalTime();
    }
}