namespace Wallet.Application;

public interface ICheckoutService
{
    Task<PaymentLinkDto> CreateLinkAsync(CreatePaymentLinkRequest request, CancellationToken ct);
    Task<CheckoutPublicDetailsDto?> GetByTokenAsync(string token, CancellationToken ct);
    Task<CheckoutReceiptDto> ProcessCheckoutAsync(string token, ProcessCheckoutRequest request, string? idempotencyKey, CancellationToken ct);
    Task<CheckoutReceiptDto> PayWithSavedCardAsync(
        Guid paymentIntentId,
        PayWithSavedCardRequest request,
        Guid userId,
        string? idempotencyKey,
        CancellationToken ct);
}
