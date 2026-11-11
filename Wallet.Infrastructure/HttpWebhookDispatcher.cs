using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wallet.Application;

namespace Wallet.Infrastructure;

public sealed class HttpWebhookDispatcher(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpWebhookDispatcher> logger) : IWebhookDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task DispatchPaymentSucceededAsync(
        string webhookUrl,
        string? webhookSecret,
        WebhookNotification notification,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        try
        {
            var json = JsonSerializer.Serialize(notification, SerializerOptions);
            var client = httpClientFactory.CreateClient("WebhookClient");
            client.Timeout = TimeSpan.FromSeconds(10);

            using var message = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var timestamp = notification.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            message.Headers.Add("X-Webhook-Event", notification.Event);
            message.Headers.Add("X-Webhook-Timestamp", timestamp);

            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
                var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
                var signatureHex = Convert.ToHexString(signatureBytes).ToLowerInvariant();
                message.Headers.Add("X-Webhook-Signature", signatureHex);
            }

            var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webhook POST to {Url} returned status code {StatusCode}", webhookUrl, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch webhook notification to {Url}", webhookUrl);
        }
    }
}
