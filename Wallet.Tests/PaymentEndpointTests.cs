using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Api.Payments;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;
public sealed class PaymentEndpointTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityTestHost Identity { get; } = new();
        public TestPaymentStore Store { get; } = new();
        public TestPaymentGateway Gateway { get; } = new();
        public PaymentSettings Settings { get; } = PaymentTestData.Settings();
        public WebApplicationFactory<Program> App { get; }
        public HttpClient Client { get; }
        public User User { get; }
        public Fixture()
        {
            User = Identity.Seed(); Store.OwnerId = User.Id;
            App = Identity.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IPaymentStore>(); s.AddSingleton<IPaymentStore>(Store);
                s.RemoveAll<IPaymentGateway>(); s.AddSingleton<IPaymentGateway>(Gateway);
                s.RemoveAll<PaymentSettings>(); s.AddSingleton(Settings);
            }));
            Client = App.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            Client.DefaultRequestHeaders.Authorization = new("Bearer", Identity.Token(User));
        }
        public async Task<PaymentResponse> Start(decimal amount = 1000m, Guid? key = null)
        {
            Client.DefaultRequestHeaders.Remove("Idempotency-Key"); Client.DefaultRequestHeaders.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
            var response = await Client.PostAsJsonAsync("/api/payments", new { amount });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
        }
        public async Task<HttpResponseMessage> Notify(string id, bool valid = true)
        {
            var requestId = Guid.NewGuid().ToString(); var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var manifest = $"id:{id};request-id:{requestId};ts:{timestamp};";
            var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Settings.WebhookSecret), Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/mercadopago/webhook?data.id={id}");
            request.Headers.Add("x-request-id", requestId); request.Headers.Add("x-signature", $"ts={timestamp},v1={(valid ? hash : new string('0', 64))}");
            request.Content = JsonContent.Create(new { type = "payment", data = new { id } });
            return await Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); await Identity.DisposeAsync(); }
    }
    [Fact]
    public async Task Start_is_idempotent_rejects_changed_amount_and_does_not_credit()
    {
        await using var f = new Fixture(); var key = Guid.NewGuid(); var first = await f.Start(key: key);
        Assert.Equal("Pending", first.Status); Assert.Equal(0, f.Store.Credits);
        using var repeat = await f.Client.PostAsJsonAsync("/api/payments", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal("true", repeat.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(first.Id, (await repeat.Content.ReadFromJsonAsync<PaymentResponse>())!.Id); Assert.Equal(1, f.Gateway.Starts);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Client.PostAsJsonAsync("/api/payments", new { amount = 999m })).StatusCode);
    }
    [Fact]
    public async Task Timeout_returns_recoverable_operation_and_repetition_does_not_start_another_checkout()
    {
        await using var f = new Fixture(); f.Gateway.Failure = new GatewayException("PROVIDER_TIMEOUT");
        f.Client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var first = await f.Client.PostAsJsonAsync("/api/payments", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var payment = (await first.Content.ReadFromJsonAsync<PaymentResponse>())!; Assert.Equal("Unknown", payment.Status);
        await f.Client.PostAsJsonAsync("/api/payments", new { amount = 1000m }); Assert.Equal(1, f.Gateway.Starts);
        f.Gateway.Failure = null; f.Gateway.Result = PaymentTestData.Verified(f.Store.Items[payment.Id]);
        using var refreshed = await f.Client.PostAsync($"/api/payments/{payment.Id}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("Approved", (await refreshed.Content.ReadFromJsonAsync<PaymentResponse>())!.Status); Assert.Equal(1, f.Store.Credits);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.001)] [InlineData(100000.01)]
    public async Task Invalid_amount_is_rejected_before_contacting_provider(decimal amount)
    {
        await using var f = new Fixture(); f.Client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Client.PostAsJsonAsync("/api/payments", new { amount })).StatusCode);
        Assert.Equal(0, f.Gateway.Starts); Assert.Empty(f.Store.Items);
    }
    [Fact]
    public async Task Invalid_signature_never_queries_provider_and_duplicates_credit_once()
    {
        await using var f = new Fixture(); var payment = await f.Start();
        f.Gateway.Result = PaymentTestData.Verified(f.Store.Items[payment.Id]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Notify("123456789", false)).StatusCode); Assert.Equal(0, f.Gateway.Gets);
        Assert.Equal(HttpStatusCode.OK, (await f.Notify("123456789")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.Notify("123456789")).StatusCode); Assert.Equal(1, f.Store.Credits);
        Assert.Equal("no-store", (await f.Client.GetAsync($"/api/payments/{payment.Id}")).Headers.CacheControl!.ToString());
    }
    [Theory]
    [InlineData("amount")] [InlineData("currency")] [InlineData("collector")] [InlineData("live")]
    public async Task Verified_but_mismatched_payment_never_credits(string mismatch)
    {
        await using var f = new Fixture(); var payment = await f.Start(); var verified = PaymentTestData.Verified(f.Store.Items[payment.Id]);
        f.Gateway.Result = mismatch switch { "amount" => verified with { Amount = 1m }, "currency" => verified with { Currency = "USD" },
            "collector" => verified with { CollectorId = "999" }, _ => verified with { LiveMode = true } };
        Assert.Equal(HttpStatusCode.Conflict, (await f.Notify("123456789")).StatusCode); Assert.Equal(0, f.Store.Credits);
    }
    [Theory]
    [InlineData(PaymentStatus.Pending)] [InlineData(PaymentStatus.Rejected)] [InlineData(PaymentStatus.Cancelled)]
    public async Task Non_approved_results_never_credit(PaymentStatus status)
    {
        await using var f = new Fixture(); var payment = await f.Start();
        f.Gateway.Result = PaymentTestData.Verified(f.Store.Items[payment.Id], status);
        Assert.Equal(HttpStatusCode.OK, (await f.Notify("123456789")).StatusCode); Assert.Equal(0, f.Store.Credits);
    }
    [Fact]
    public async Task Persistence_failure_is_not_acknowledged_as_processed_and_retry_can_recover()
    {
        await using var f = new Fixture(); var payment = await f.Start(); f.Gateway.Result = PaymentTestData.Verified(f.Store.Items[payment.Id]);
        f.Store.FailApply = true; Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Notify("123456789")).StatusCode);
        f.Store.FailApply = false; Assert.Equal(HttpStatusCode.OK, (await f.Notify("123456789")).StatusCode); Assert.Equal(1, f.Store.Credits);
    }
    [Fact]
    public async Task Ownership_authentication_disabled_configuration_and_openapi_are_enforced()
    {
        await using var f = new Fixture(); var payment = await f.Start();
        var other = f.Identity.Seed("other@example.com"); f.Client.DefaultRequestHeaders.Authorization = new("Bearer", f.Identity.Token(other));
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync($"/api/payments/{payment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.PostAsync($"/api/payments/{payment.Id}/refresh", null)).StatusCode);
        f.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Client.GetAsync($"/api/payments/{payment.Id}")).StatusCode);
        using var document = JsonDocument.Parse(await f.Client.GetStringAsync("/openapi/v1.json"));
        Assert.True(document.RootElement.GetProperty("paths").GetProperty("/api/payments").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(document.RootElement.GetProperty("paths").GetProperty("/api/payments/mercadopago/webhook").GetProperty("post").TryGetProperty("security", out _));
        f.Client.DefaultRequestHeaders.Authorization = new("Bearer", f.Identity.Token(f.User)); f.Settings.Enabled = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await f.Client.GetAsync($"/api/payments/{payment.Id}")).StatusCode);
    }
    [Fact]
    public async Task Returning_from_checkout_cannot_approve_or_credit()
    {
        await using var f = new Fixture(); await f.Start();
        Assert.Equal(HttpStatusCode.OK, (await f.Client.GetAsync("/payments/return?status=approved&payment_id=123456789")).StatusCode);
        Assert.Equal(0, f.Store.Credits); Assert.Equal(0, f.Gateway.Gets);
    }
    [Fact]
    public void Signature_rejects_expired_ambiguous_or_tampered_manifests()
    {
        var settings = PaymentTestData.Settings(); var signature = new MercadoPagoSignature(settings, TimeProvider.System);
        var request = Guid.NewGuid().ToString(); var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var digest = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.WebhookSecret),
            Encoding.UTF8.GetBytes($"id:123;request-id:{request};ts:{timestamp};")));
        Assert.True(signature.Verify("123", request, $"ts={timestamp},v1={digest}"));
        Assert.False(signature.Verify("124", request, $"ts={timestamp},v1={digest}"));
        Assert.False(signature.Verify("123", request, $"ts=1000000000,v1={digest}"));
        Assert.False(signature.Verify("123", request, $"ts={timestamp},ts={timestamp},v1={digest}"));
    }
}
