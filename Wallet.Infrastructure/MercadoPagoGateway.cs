using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wallet.Application;
using Wallet.Domain;

namespace Wallet.Infrastructure;

// Cliente HTTP sin reintentos automáticos de POST: crear preferencias no presupone idempotencia del proveedor.
public sealed class MercadoPagoGateway(HttpClient http, PaymentSettings settings) : IPaymentGateway
{
    public async Task<CheckoutResult> StartAsync(CheckoutRequest request, CancellationToken ct)
    {
        var root = settings.PublicBaseUrl.TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, "checkout/preferences");
        message.Content = JsonContent.Create(new
        {
            items = new[] { new { id = request.Reference.ToString("D"), title = "Carga de saldo de prueba",
                quantity = 1, currency_id = request.Currency, unit_price = request.Amount } },
            external_reference = request.Reference.ToString("D"),
            // Checkout Pro puede enviar IPN además de Webhooks si no filtro la fuente.
            notification_url = root + "/api/payments/mercadopago/webhook?source_news=webhooks",
            back_urls = new { success = root + "/payments/return", pending = root + "/payments/return", failure = root + "/payments/return" }
        });
        using var json = await SendAsync(message, ct);
        try
        {
            var value = json.RootElement;
            var id = value.GetProperty("id").GetString()!;
            var url = value.GetProperty("sandbox_init_point").GetString()!;
            // Sólo el checkout sandbox del proveedor; no se aceptan enlaces arbitrarios para el navegador.
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !IsSandboxCheckout(url))
                throw new GatewayException("PROVIDER_INVALID_RESPONSE");
            return new(id, url);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new GatewayException("PROVIDER_INVALID_RESPONSE"); }
    }
    public static bool IsSandboxCheckout(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.Host == "sandbox.mercadopago.com.ar"
        && string.IsNullOrEmpty(uri.UserInfo);
    public async Task<VerifiedPayment> GetAsync(string paymentId, CancellationToken ct)
    {
        if (paymentId.Length is < 1 or > 64 || !paymentId.All(char.IsAsciiDigit))
            throw new GatewayException("PROVIDER_INVALID_ID", true);
        using var message = new HttpRequestMessage(HttpMethod.Get, "v1/payments/" + paymentId);
        using var json = await SendAsync(message, ct); var payment = Parse(json.RootElement);
        if (payment.Id != paymentId) throw new GatewayException("PROVIDER_INVALID_RESPONSE");
        if (payment.LiveMode)
        {
            // Algunos pagos entre usuarios de prueba informan live_mode=true.
            // Verifico las identidades mediante la API; nunca confío en el navegador ni en el prefijo del token.
            string payerId;
            try { payerId = Scalar(json.RootElement.GetProperty("payer"), "id"); }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException)
            { throw new GatewayException("PROVIDER_TEST_ACCOUNTS_UNVERIFIED"); }
            if (payerId.Length is < 1 or > 32 || !payerId.All(char.IsAsciiDigit) || payerId == payment.CollectorId)
                throw new GatewayException("PROVIDER_TEST_ACCOUNTS_UNVERIFIED");
            using var sellerRequest = new HttpRequestMessage(HttpMethod.Get, "users/me");
            using var seller = await SendAsync(sellerRequest, ct);
            if (!IsTestUser(seller.RootElement, payment.CollectorId) || payment.CollectorId != settings.CollectorId)
                throw new GatewayException("PROVIDER_TEST_ACCOUNTS_UNVERIFIED");
            using var buyerRequest = new HttpRequestMessage(HttpMethod.Get, "users/" + payerId);
            using var buyer = await SendAsync(buyerRequest, ct);
            if (!IsTestUser(buyer.RootElement, payerId) && !IsConfiguredTestBuyer(buyer.RootElement, payerId))
                throw new GatewayException("PROVIDER_TEST_ACCOUNTS_UNVERIFIED");
            payment = payment with { TestAccountsVerified = true };
        }
        return payment;
    }
    private bool IsConfiguredTestBuyer(JsonElement buyer, string expectedId)
    {
        // El perfil público puede omitir tags. En ese caso exijo un ID confirmado en
        // Cuentas de prueba y configurado por el operador; nunca se toma del request del cliente.
        try
        {
            return Scalar(buyer, "id") == expectedId && !buyer.TryGetProperty("tags", out _)
                && settings.TestBuyerIds.Contains(expectedId, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException) { return false; }
    }
    private static bool IsTestUser(JsonElement user, string expectedId)
    {
        try
        {
            return Scalar(user, "id") == expectedId && user.GetProperty("tags").ValueKind == JsonValueKind.Array
                && user.GetProperty("tags").EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "test_user");
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException) { return false; }
    }
    public async Task<IReadOnlyList<VerifiedPayment>> SearchAsync(Guid reference, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get,
            $"v1/payments/search?external_reference={reference:D}&limit=100&sort=date_created&criteria=desc");
        using var json = await SendAsync(message, ct);
        try
        {
            // No se decide a partir de una página incompleta de intentos.
            if (json.RootElement.GetProperty("paging").GetProperty("total").GetInt32() > 100)
                throw new GatewayException("PROVIDER_SEARCH_REQUIRES_REVIEW");
            var results = json.RootElement.GetProperty("results").EnumerateArray().Select(Parse).ToArray();
            if (results.Any(x => x.Reference != reference)) throw new GatewayException("PROVIDER_INVALID_RESPONSE");
            return results;
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new GatewayException("PROVIDER_INVALID_RESPONSE"); }
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new GatewayException("PROVIDER_HTTP_ERROR", (int)response.StatusCode is >= 400 and < 500
                    && response.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests));
            // Respuestas acotadas; tampoco se registran el cuerpo ni el encabezado Authorization.
            await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GatewayException("PROVIDER_TIMEOUT"); }
        catch (HttpRequestException) { throw new GatewayException("PROVIDER_UNAVAILABLE"); }
        catch (JsonException) { throw new GatewayException("PROVIDER_INVALID_RESPONSE"); }
    }
    private static string Scalar(JsonElement e, string name)
    {
        var p = e.GetProperty(name); return p.ValueKind == JsonValueKind.String ? p.GetString()! : p.GetRawText();
    }
    private static VerifiedPayment Parse(JsonElement e)
    {
        try
        {
            var status = Scalar(e, "status") switch
            {
                "approved" => PaymentStatus.Approved,
                "pending" or "in_process" or "authorized" => PaymentStatus.Pending,
                "rejected" => PaymentStatus.Rejected,
                "cancelled" => PaymentStatus.Cancelled,
                // Reembolsos y contracargos requieren otro proceso; no se borran créditos anteriores.
                _ => throw new GatewayException("PROVIDER_STATUS_REQUIRES_REVIEW")
            };
            var id = Scalar(e, "id"); var reference = Guid.Parse(Scalar(e, "external_reference"));
            var amount = e.GetProperty("transaction_amount").GetDecimal();
            if (e.GetProperty("transaction_amount_refunded").GetDecimal() != 0)
                throw new GatewayException("PROVIDER_STATUS_REQUIRES_REVIEW");
            var currency = Scalar(e, "currency_id"); var collector = Scalar(e, "collector_id");
            var live = e.GetProperty("live_mode").GetBoolean();
            var updated = Scalar(e, "date_last_updated");
            MoneyRules.ValidatePositive(amount);
            if (id.Length is < 1 or > 64 || !id.All(char.IsAsciiDigit) || reference == Guid.Empty
                || !DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.None, out var providerUpdatedAt))
                throw new GatewayException("PROVIDER_INVALID_RESPONSE");
            var eventKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"MercadoPago|{id}|{status}|{updated}")));
            return new(id, reference, amount, currency, collector, live, status, eventKey, providerUpdatedAt);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { throw new GatewayException("PROVIDER_INVALID_RESPONSE"); }
    }
}
