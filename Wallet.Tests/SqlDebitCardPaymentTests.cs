using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlDebitCardPaymentTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!, sql => sql.MaxBatchSize(1))
            .AddInterceptors(interceptors).Options;

    private sealed class Fixture : IAsyncDisposable
    {
        public SqlTransferTests.Pair Pair { get; private set; } = null!;
        public Card Card { get; private set; } = null!;
        public PaymentLink Link { get; private set; } = null!;
        public static async Task<Fixture> Create(decimal balance = 1000m, decimal amount = 300m)
        {
            var pair = await SqlTransferTests.Pair.Create(balance);
            var now = DateTimeOffset.UtcNow;
            var card = Card.Create(pair.Sender.Id, "Titular Debit", "VISA DEBITO", CardType.Debit, "1234", 12, now.Year + 5, now);
            var link = PaymentLink.Create(pair.Destination.Id, amount, "Compra", null, PaymentLinkType.SingleUse, now, merchantOwnerId: pair.Receiver.Id);
            await using var db = new WalletDbContext(Options());
            db.Cards.Add(card); db.PaymentLinks.Add(link); await db.SaveChangesAsync();
            return new() { Pair = pair, Card = card, Link = link };
        }
        public Task<CheckoutReceiptDto> Pay(string key, params IInterceptor[] interceptors) =>
            new SqlDebitCardPaymentStore(Options(interceptors), TimeProvider.System)
                .ExecuteAsync(new(Link.Id, Card.Id, Pair.Sender.Id, key), CancellationToken.None);
        public async Task AssertUnchanged(decimal sourceBalance)
        {
            await using var db = new WalletDbContext(Options());
            Assert.Equal(sourceBalance, (await db.Wallets.SingleAsync(w => w.Id == Pair.Source.Id)).Balance.Amount);
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == Pair.Destination.Id)).Balance.Amount);
            Assert.False(await db.Payments.AnyAsync(p => p.PaymentLinkId == Link.Id));
            Assert.False(await db.Transfers.AnyAsync(t => t.SourceWalletId == Pair.Source.Id));
            Assert.False(await db.Movements.AnyAsync(m => m.TransferId != null && m.WalletId == Pair.Source.Id));
            Assert.Equal(PaymentLinkStatus.Active, (await db.PaymentLinks.SingleAsync(l => l.Id == Link.Id)).Status);
        }
        public async ValueTask DisposeAsync()
        {
            await using (var db = new WalletDbContext(Options()))
            {
                await db.Payments.Where(p => p.PaymentLinkId == Link.Id).ExecuteDeleteAsync();
                await db.PaymentLinks.Where(l => l.Id == Link.Id).ExecuteDeleteAsync();
                await db.Cards.Where(c => c.Id == Card.Id).ExecuteDeleteAsync();
            }
            await Pair.DisposeAsync();
        }
    }

    [AzureSqlFact]
    public async Task Debit_card_debits_wallet_credits_merchant_and_replays_without_second_debit()
    {
        await using var f = await Fixture.Create(balance: 300m);
        var receipt = await f.Pay("purchase-1");
        await using var db = new WalletDbContext(Options());
        var payment = await db.Payments.SingleAsync(p => p.Id == receipt.PaymentId);
        Assert.Equal(PaymentMethod.Card, payment.Method);
        Assert.Equal(f.Pair.Source.Id, payment.PayerWalletId);
        Assert.Equal(f.Pair.Sender.Id, payment.PayerUserId);
        Assert.NotNull(payment.TransferId);
        Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
        Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Destination.Id)).Balance.Amount);
        Assert.Equal(2, await db.Movements.CountAsync(m => m.TransferId == payment.TransferId));
        var replay = await f.Pay("purchase-1");
        Assert.True(replay.IdempotencyReplayed);
        Assert.Equal(receipt.PaymentId, replay.PaymentId);
        Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == f.Pair.Source.Id));
        Assert.False(await db.DevelopmentFundings.AnyAsync(x => x.WalletId == f.Pair.Destination.Id));
    }

    [AzureSqlFact]
    public async Task Insufficient_funds_leave_every_record_unchanged_and_allow_retry_after_funding()
    {
        await using var f = await Fixture.Create(balance: 100m);
        var error = await Assert.ThrowsAsync<TransferRejectedException>(() => f.Pay("retry-after-funding"));
        Assert.Equal("INSUFFICIENT_FUNDS", error.Code);
        await f.AssertUnchanged(100m);
        await new DevelopmentFundingService(new SqlDevelopmentFundingStore(Options(), TimeProvider.System))
            .FundAsync(new(f.Pair.Source.Id, 200m, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal("Completed", (await f.Pay("retry-after-funding")).Status);
    }

    [AzureSqlFact]
    public async Task Concurrent_duplicate_requests_commit_only_one_payment()
    {
        await using var f = await Fixture.Create();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Pay("duplicate")));
        Assert.Single(receipts.Select(r => r.PaymentId).Distinct());
        Assert.Single(receipts, r => !r.IdempotencyReplayed);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.PaymentLinkId == f.Link.Id));
    }

    [AzureSqlFact]
    public async Task Single_use_link_cannot_be_paid_twice_using_different_keys()
    {
        await using var f = await Fixture.Create();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            try { await f.Pay($"attempt-{i}"); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.Single(results, success => success);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
    }

    private sealed class FailPaymentInsert : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO [Payments]", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("Injected payment insert failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }

    [AzureSqlFact]
    public async Task Failure_recording_payment_rolls_back_the_already_executed_debit_and_credit()
    {
        await using var f = await Fixture.Create();
        var failure = new FailPaymentInsert();
        Assert.NotNull(await Record.ExceptionAsync(() => f.Pay("atomic-failure", failure)));
        Assert.True(failure.Fired);
        await f.AssertUnchanged(1000m);
        Assert.Equal("Completed", (await f.Pay("atomic-failure")).Status);
    }

    [AzureSqlFact]
    public async Task Card_id_and_user_are_part_of_the_idempotency_identity()
    {
        await using var f = await Fixture.Create();
        await f.Pay("same-key");
        await using var db = new WalletDbContext(Options());
        var secondCard = Card.Create(f.Pair.Sender.Id, "Titular", "Visa", CardType.Debit, "1234", 12, DateTimeOffset.UtcNow.Year + 5, DateTimeOffset.UtcNow);
        db.Cards.Add(secondCard); await db.SaveChangesAsync();
        try
        {
            var store = new SqlDebitCardPaymentStore(Options(), TimeProvider.System);
            var error = await Assert.ThrowsAsync<TransferRejectedException>(() => store.ExecuteAsync(
                new(f.Link.Id, secondCard.Id, f.Pair.Sender.Id, "same-key"), CancellationToken.None));
            Assert.Equal("IDEMPOTENCY_CONFLICT", error.Code);
        }
        finally { await db.Cards.Where(c => c.Id == secondCard.Id).ExecuteDeleteAsync(); }
    }
    [AzureSqlFact]
    public async Task Authenticated_HTTP_debit_payment_and_legacy_replay_preserve_balance()
    {
        await using var f = await Fixture.Create();
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(f.Pair.Sender));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-purchase");
        var response = await client.PostAsJsonAsync($"/api/v1/me/payment-links/{f.Link.Id}/card-payments", new { cardId = f.Card.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<CheckoutReceiptDto>())!;
        var replay = await client.PostAsJsonAsync($"/api/v1/checkout/{f.Link.Id}/pay", new { cardId = f.Card.Id });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<CheckoutReceiptDto>())!.IdempotencyReplayed);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
        Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Destination.Id)).Balance.Amount);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.Id == receipt.PaymentId));
    }

    [AzureSqlFact]
    public async Task HTTP_insufficient_funds_returns_conflict_without_crediting_merchant()
    {
        await using var f = await Fixture.Create(balance: 100m);
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(f.Pair.Sender));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-insufficient");
        var response = await client.PostAsJsonAsync($"/api/v1/me/payment-links/{f.Link.Id}/card-payments", new { cardId = f.Card.Id });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("INSUFFICIENT_FUNDS", await response.Content.ReadAsStringAsync());
        await f.AssertUnchanged(100m);
    }

    [AzureSqlFact]
    public async Task Physical_card_order_accepts_structured_address_and_keeps_it_scoped_to_owner()
    {
        await using var f = await Fixture.Create();
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(f.Pair.Sender));
        var response = await client.PostAsJsonAsync("/api/v1/me/physical-card-orders", new
        {
            type = "Debit",
            deliveryAddress = new { street = "Av. Corrientes", number = "1234", city = "CABA", state = "Buenos Aires", postalCode = "1043", country = "Argentina" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<CardRequestDto>())!;
        try
        {
            Assert.Equal("AwaitingAddressValidation", order.Status);
            Assert.Equal("Pending", order.DeliveryAddress!.ValidationStatus);
            Assert.Contains("/api/v1/me/physical-card-orders/", response.Headers.Location!.ToString());
            var status = await client.GetFromJsonAsync<CardRequestDto>($"/api/v1/me/physical-card-orders/{order.Id}");
            Assert.Equal(order, status);
            client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(f.Pair.Receiver));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/me/physical-card-orders/{order.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/me/physical-card-orders", new { addressId = order.AddressId })).StatusCode);
            await using var db = new WalletDbContext(Options());
            Assert.Equal(f.Pair.Sender.Id, (await db.Addresses.SingleAsync(a => a.Id == order.AddressId)).UserId);
        }
        finally
        {
            await using var db = new WalletDbContext(Options());
            await db.CardRequests.Where(r => r.Id == order.Id).ExecuteDeleteAsync();
            await db.Addresses.Where(a => a.Id == order.AddressId).ExecuteDeleteAsync();
        }
    }    [AzureSqlFact]
    public async Task Different_links_cannot_bypass_idempotency_or_spend_the_same_funds_twice()
    {
        foreach (var sameKey in new[] { true, false })
        {
            await using var f = await Fixture.Create(balance: 500m);
            var secondLink = PaymentLink.Create(f.Pair.Destination.Id, 300m, "Otra compra", null,
                PaymentLinkType.SingleUse, DateTimeOffset.UtcNow, merchantOwnerId: f.Pair.Receiver.Id);
            await using (var db = new WalletDbContext(Options()))
            { db.PaymentLinks.Add(secondLink); await db.SaveChangesAsync(); }
            try
            {
                async Task<string> Attempt(Guid linkId, string key)
                {
                    try
                    {
                        await new SqlDebitCardPaymentStore(Options(), TimeProvider.System)
                            .ExecuteAsync(new(linkId, f.Card.Id, f.Pair.Sender.Id, key), CancellationToken.None);
                        return "OK";
                    }
                    catch (TransferRejectedException e) { return e.Code; }
                }
                var outcomes = await Task.WhenAll(Attempt(f.Link.Id, "competing-key"), Attempt(secondLink.Id, sameKey ? "competing-key" : "another-key"));
                Assert.Single(outcomes, x => x == "OK");
                Assert.Contains(sameKey ? "IDEMPOTENCY_CONFLICT" : "INSUFFICIENT_FUNDS", outcomes);
                await using var db = new WalletDbContext(Options());
                Assert.Equal(200m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
                Assert.Equal(300m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Destination.Id)).Balance.Amount);
                Assert.Equal(1, await db.Transfers.CountAsync(t => t.SourceWalletId == f.Pair.Source.Id));
            }
            finally
            {
                await using var db = new WalletDbContext(Options());
                await db.Payments.Where(p => p.PaymentLinkId == secondLink.Id).ExecuteDeleteAsync();
                await db.PaymentLinks.Where(p => p.Id == secondLink.Id).ExecuteDeleteAsync();
            }
        }
    }

    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        {
            Fired = true;
            throw new InvalidOperationException("Injected lost commit confirmation.");
        }
    }

    [AzureSqlFact]
    public async Task Lost_commit_confirmation_is_replayed_without_another_debit()
    {
        await using var f = await Fixture.Create();
        var failure = new FailAfterCommit();
        Assert.NotNull(await Record.ExceptionAsync(() => f.Pay("lost-confirmation", failure)));
        Assert.True(failure.Fired);
        var recovered = await f.Pay("lost-confirmation");
        Assert.True(recovered.IdempotencyReplayed);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(700m, (await db.Wallets.SingleAsync(w => w.Id == f.Pair.Source.Id)).Balance.Amount);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.PaymentLinkId == f.Link.Id));
    }}
