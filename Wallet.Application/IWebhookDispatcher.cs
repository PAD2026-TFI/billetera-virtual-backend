namespace Wallet.Application;

public sealed record WebhookPaymentData(
    Guid PaymentId,
    Guid PaymentLinkId,
    string Token,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentMethod,
    string? AuthorizationCode,
    Guid DestinationWalletId,
    DateTimeOffset CompletedAt);

public sealed record WebhookNotification(
    string Event,
    WebhookPaymentData Data,
    DateTimeOffset Timestamp);

public interface IWebhookDispatcher
{
    Task DispatchPaymentSucceededAsync(
        string webhookUrl,
        string? webhookSecret,
        WebhookNotification notification,
        CancellationToken ct = default);
}
