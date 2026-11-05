namespace Wallet.Domain;

public enum PaymentStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Cancelled
}
