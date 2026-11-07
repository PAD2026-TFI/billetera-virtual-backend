namespace Wallet.Application;

public sealed record DebitCardPaymentCommand(Guid PaymentLinkId, Guid CardId, Guid UserId, string IdempotencyKey);

// The implementation commits the debit, credit, movements, payment and link status together.
public interface IDebitCardPaymentStore
{
    Task<CheckoutReceiptDto> ExecuteAsync(DebitCardPaymentCommand command, CancellationToken ct);
}
