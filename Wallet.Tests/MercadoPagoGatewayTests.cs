using System.Net;
using System.Text;
using System.Text.Json;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;
public sealed class MercadoPagoGatewayTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request, ct); }
    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static MercadoPagoGateway Gateway(Handler handler, PaymentSettings? settings = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com/") }, settings ?? PaymentTestData.Settings());
    [Fact]
    public async Task Checkout_uses_server_reference_amount_and_urls_without_card_data_or_automatic_retries()
    {
        var reference = Guid.NewGuid(); var calls = 0;
        using var handler = new Handler(async (request, ct) =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/checkout/preferences", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(reference.ToString("D"), body.RootElement.GetProperty("external_reference").GetString());
            Assert.Equal(1000m, body.RootElement.GetProperty("items")[0].GetProperty("unit_price").GetDecimal());
            Assert.Equal("https://wallet.example.com/api/payments/mercadopago/webhook", body.RootElement.GetProperty("notification_url").GetString());
            return Json("""{"id":"pref-123","sandbox_init_point":"https://sandbox.mercadopago.com.ar/checkout/v1/redirect?pref_id=pref-123"}""");
        });
        var result = await Gateway(handler).StartAsync(new(reference, 1000m, "ARS"), CancellationToken.None);
        Assert.Equal("pref-123", result.PreferenceId); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("approved", PaymentStatus.Approved)] [InlineData("pending", PaymentStatus.Pending)]
    [InlineData("in_process", PaymentStatus.Pending)] [InlineData("rejected", PaymentStatus.Rejected)]
    [InlineData("cancelled", PaymentStatus.Cancelled)]
    public async Task Payment_response_maps_verified_fields(string externalStatus, PaymentStatus expected)
    {
        var reference = Guid.NewGuid();
        using var handler = new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new
        {
            id = 123456789, external_reference = reference, transaction_amount = 1000m, transaction_amount_refunded = 0m, currency_id = "ARS",
            collector_id = 123456, live_mode = false, status = externalStatus, date_last_updated = "2026-10-28T00:00:00Z"
        }))));
        var result = await Gateway(handler).GetAsync("123456789", CancellationToken.None);
        Assert.Equal(expected, result.Status); Assert.Equal(reference, result.Reference); Assert.False(result.LiveMode);
        Assert.Equal(64, result.EventKey.Length); Assert.Equal("123456", result.CollectorId);
    }
    [Theory]
    [InlineData(400, true)] [InlineData(401, true)] [InlineData(429, false)] [InlineData(500, false)]
    public async Task Http_errors_are_sanitized_and_post_is_not_retried(int code, bool definitive)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json("secret provider body", (HttpStatusCode)code)); });
        var ex = await Assert.ThrowsAsync<GatewayException>(() => Gateway(handler).StartAsync(new(Guid.NewGuid(), 10, "ARS"), CancellationToken.None));
        Assert.Equal(definitive, ex.Definitive); Assert.Equal("PROVIDER_HTTP_ERROR", ex.Code); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Timeout_is_uncertain_and_caller_cancellation_is_preserved()
    {
        using var handler = new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); });
        var settings = PaymentTestData.Settings(); settings.TimeoutSeconds = 1;
        Assert.Equal("PROVIDER_TIMEOUT", (await Assert.ThrowsAsync<GatewayException>(() =>
            Gateway(handler, settings).GetAsync("1", CancellationToken.None))).Code);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(handler).GetAsync("1", cancel.Token));
    }
    [Theory]
    [InlineData("https://evil.example/checkout")]
    [InlineData("https://sandbox.mercadopago.com.ar.evil.example/checkout")]
    [InlineData("http://sandbox.mercadopago.com.ar/checkout")]
    public async Task Untrusted_checkout_urls_are_rejected(string url)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new { id = "p", sandbox_init_point = url }))));
        await Assert.ThrowsAsync<GatewayException>(() => Gateway(handler).StartAsync(new(Guid.NewGuid(), 10, "ARS"), CancellationToken.None));
    }
    [Fact]
    public async Task Incomplete_search_is_not_used_to_decide_a_payment_result()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("""{"paging":{"total":101},"results":[]}""")));
        Assert.Equal("PROVIDER_SEARCH_REQUIRES_REVIEW", (await Assert.ThrowsAsync<GatewayException>(() =>
            Gateway(handler).SearchAsync(Guid.NewGuid(), CancellationToken.None))).Code);
    }
    [Fact]
    public async Task Partial_refund_is_not_mistaken_for_a_full_creditable_approval()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(JsonSerializer.Serialize(new
        {
            id = 123456789, external_reference = Guid.NewGuid(), transaction_amount = 1000m, transaction_amount_refunded = 100m,
            currency_id = "ARS", collector_id = 123456, live_mode = false, status = "approved", date_last_updated = "2026-10-28T00:00:00Z"
        }))));
        Assert.Equal("PROVIDER_STATUS_REQUIRES_REVIEW", (await Assert.ThrowsAsync<GatewayException>(() =>
            Gateway(handler).GetAsync("123456789", CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Live_flag_requires_verified_test_seller_and_buyer(bool testSeller, bool testBuyer, bool allowed)
    {
        using var handler = new Handler((r, _) =>
        {
            var body = r.RequestUri!.AbsolutePath switch
            {
                "/v1/payments/123" => JsonSerializer.Serialize(new { id = 123, external_reference = Guid.NewGuid(),
                    transaction_amount = 1000m, transaction_amount_refunded = 0m, currency_id = "ARS", collector_id = 123456,
                    live_mode = true, payer = new { id = 789 }, status = "approved", date_last_updated = "2026-10-28T00:00:00Z" }),
                "/users/me" => JsonSerializer.Serialize(new { id = 123456, tags = testSeller ? new[] { "test_user" } : Array.Empty<string>() }),
                "/users/789" => JsonSerializer.Serialize(new { id = 789, tags = testBuyer ? new[] { "test_user" } : Array.Empty<string>() }),
                _ => throw new InvalidOperationException()
            };
            return Task.FromResult(Json(body));
        });
        if (allowed) Assert.True((await Gateway(handler).GetAsync("123", CancellationToken.None)).TestAccountsVerified);
        else Assert.Equal("PROVIDER_TEST_ACCOUNTS_UNVERIFIED", (await Assert.ThrowsAsync<GatewayException>(() =>
            Gateway(handler).GetAsync("123", CancellationToken.None))).Code);
    }

}
