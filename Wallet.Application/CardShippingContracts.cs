namespace Wallet.Application;

// Integration contracts only: no provider is registered until a real adapter is implemented.
public sealed record DeliveryDestination(
    string Street, string Number, string City, string State, string PostalCode, string Country);

public sealed record GeocodingResult(
    bool IsValid, decimal? Latitude, decimal? Longitude, string? ExternalPlaceId);

public interface IAddressGeocoder
{
    Task<GeocodingResult> ValidateAsync(DeliveryDestination destination, CancellationToken ct);
}

public enum CardShipmentStatus { Requested, Dispatched, InTransit, Delivered, Failed, Returned }

public sealed record CardShipmentReference(string Provider, string ExternalShipmentId, string TrackingNumber);

public sealed record CardShipmentEvent(
    string ExternalEventId, CardShipmentStatus Status, DateTimeOffset OccurredAt);

public interface ICardShippingProvider
{
    // The order ID is the stable idempotency key across retries.
    Task<CardShipmentReference> CreateShipmentAsync(
        Guid cardRequestId, DeliveryDestination destination, CancellationToken ct);
    Task<IReadOnlyList<CardShipmentEvent>> GetTrackingAsync(
        CardShipmentReference shipment, CancellationToken ct);
}
