using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

public sealed class WebhookDispatcherTests
{
    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    [Fact]
    public async Task HttpWebhookDispatcher_sends_post_with_headers_and_valid_signature()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new DelegatingHandlerStub(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var factory = new TestHttpClientFactory(handler);
        var dispatcher = new HttpWebhookDispatcher(factory, NullLogger<HttpWebhookDispatcher>.Instance);

        var secret = new string('s', 32);
        var now = DateTimeOffset.UtcNow;
        var notification = new WebhookNotification(
            "payment.succeeded",
            new WebhookPaymentData(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "test-token",
                500.00m,
                "ARS",
                "Completed",
                "Card",
                "AUTH-123",
                Guid.NewGuid(),
                now),
            now);

        await dispatcher.DispatchPaymentSucceededAsync(
            "https://merchant.example.com/api/webhook",
            secret,
            notification);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://merchant.example.com/api/webhook", capturedRequest.RequestUri?.ToString());
        Assert.Equal("payment.succeeded", capturedRequest.Headers.GetValues("X-Webhook-Event").Single());

        var signature = capturedRequest.Headers.GetValues("X-Webhook-Signature").Single();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(capturedBody!))).ToLowerInvariant();
        Assert.Equal(expectedHash, signature);
    }

    [Fact]
    public async Task HttpWebhookDispatcher_does_not_throw_when_endpoint_fails()
    {
        var handler = new DelegatingHandlerStub(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var factory = new TestHttpClientFactory(handler);
        var dispatcher = new HttpWebhookDispatcher(factory, NullLogger<HttpWebhookDispatcher>.Instance);

        var notification = new WebhookNotification(
            "payment.succeeded",
            new WebhookPaymentData(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "test-token",
                100m,
                "ARS",
                "Completed",
                "Card",
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);

        var exception = await Record.ExceptionAsync(() => dispatcher.DispatchPaymentSucceededAsync(
            "https://merchant.example.com/api/webhook",
            new string('a', 32),
            notification));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GatewayService_TestWebhookAsync_fails_when_webhook_not_configured()
    {
        var clients = new InMemoryExternalClientRepo();
        var wallets = new InMemoryWalletRepo();
        var timeProvider = TimeProvider.System;
        var gateway = new GatewayService(clients, wallets, timeProvider);

        var client = ExternalClient.Create("Empresa S.A.", "30712345679", "admin@empresa.com", DateTimeOffset.UtcNow);
        await clients.CreateAsync(client, CancellationToken.None);

        var mockDispatcher = new RecordingWebhookDispatcher();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.TestWebhookAsync(client.Id, mockDispatcher, CancellationToken.None));
    }

    [Fact]
    public async Task GatewayService_TestWebhookAsync_dispatches_when_configured()
    {
        var clients = new InMemoryExternalClientRepo();
        var wallets = new InMemoryWalletRepo();
        var timeProvider = TimeProvider.System;
        var gateway = new GatewayService(clients, wallets, timeProvider);

        var client = ExternalClient.Create("Empresa S.A.", "30712345679", "admin@empresa.com", DateTimeOffset.UtcNow);
        client.ConfigureWebhook("https://empresa.com/webhook", new string('k', 32));
        await clients.CreateAsync(client, CancellationToken.None);

        var mockDispatcher = new RecordingWebhookDispatcher();

        var result = await gateway.TestWebhookAsync(client.Id, mockDispatcher, CancellationToken.None);

        Assert.True(result);
        Assert.Single(mockDispatcher.Dispatched);
        Assert.Equal("https://empresa.com/webhook", mockDispatcher.Dispatched[0].Url);
        Assert.Equal("webhook.test", mockDispatcher.Dispatched[0].Notification.Event);
    }

    private sealed class RecordingWebhookDispatcher : IWebhookDispatcher
    {
        public List<(string Url, string? Secret, WebhookNotification Notification)> Dispatched { get; } = [];

        public Task DispatchPaymentSucceededAsync(
            string webhookUrl,
            string? webhookSecret,
            WebhookNotification notification,
            CancellationToken ct = default)
        {
            Dispatched.Add((webhookUrl, webhookSecret, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryExternalClientRepo : IExternalClientRepository
    {
        private readonly Dictionary<Guid, ExternalClient> _clients = [];

        public Task<ExternalClient?> FindByApiKeyHashAsync(string apiKeyHash, CancellationToken ct) =>
            Task.FromResult(_clients.Values.FirstOrDefault(c => c.ApiKeyHash == apiKeyHash));

        public Task<ExternalClient?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_clients.GetValueOrDefault(id));

        public Task CreateAsync(ExternalClient client, CancellationToken ct)
        {
            _clients[client.Id] = client;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ExternalClient client, CancellationToken ct)
        {
            _clients[client.Id] = client;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryWalletRepo : IWalletRepository
    {
        private readonly Dictionary<Guid, Wallet.Domain.Wallet> _wallets = [];

        public Task<Wallet.Domain.Wallet?> FindByOwnerIdAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_wallets.Values.FirstOrDefault(w => w.OwnerId == ownerId));

        public Task<Wallet.Domain.Wallet?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_wallets.GetValueOrDefault(id));

        public Task<Wallet.Domain.Wallet?> FindByCvuAsync(string cvu, CancellationToken ct) =>
            Task.FromResult(_wallets.Values.FirstOrDefault(w => w.Cvu == cvu));

        public Task<Wallet.Domain.Wallet?> FindByAliasAsync(string alias, CancellationToken ct) =>
            Task.FromResult(_wallets.Values.FirstOrDefault(w => w.Alias == alias));

        public Task<Wallet.Domain.Wallet?> GetByUserIdAsync(Guid userId, CancellationToken ct) =>
            FindByOwnerIdAsync(userId, ct);

        public Task<WalletCreation> CreateOrGetAsync(Wallet.Domain.Wallet wallet, CancellationToken ct)
        {
            _wallets[wallet.Id] = wallet;
            return Task.FromResult(new WalletCreation(wallet, true));
        }

        public Task UpdateAsync(Wallet.Domain.Wallet wallet, CancellationToken ct)
        {
            _wallets[wallet.Id] = wallet;
            return Task.CompletedTask;
        }
    }
}
