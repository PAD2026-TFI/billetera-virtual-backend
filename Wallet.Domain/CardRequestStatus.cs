namespace Wallet.Domain;

public enum CardRequestStatus
{
    Pending,
    Processing,
    Issued,
    InTransit,
    Delivered,
    Cancelled,
    Rejected,
    AwaitingAddressValidation
}
