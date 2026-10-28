using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;
[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlPaymentTests
{
    private static DbContextOptions<WalletDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")!,
            sql => sql.MaxBatchSize(1)).AddInterceptors(interceptors).Options;
    private static SqlPaymentStore Store(params IInterceptor[] interceptors) => new(Options(interceptors), TimeProvider.System);
    private sealed class Fixture : IAsyncDisposable
    {
        public User User { get; } = User.Create("Payment", "SQL", $"pay-{Guid.NewGuid():N}@example.com", DateTimeOffset.UtcNow);
        public DomainWallet Wallet { get; }
        private Fixture() { Wallet = DomainWallet.Create(User.Id, DateTimeOffset.UtcNow); User.SetPasswordHash("test-hash-not-authenticated"); }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await using var db = new WalletDbContext(Options());
            db.Users.Add(f.User); db.Wallets.Add(f.Wallet); await db.SaveChangesAsync(); return f;
        }
        public async Task<Payment> Payment(decimal amount = 1000m, Guid? key = null) =>
            (await Store().CreateAsync(User.Id, amount, key ?? Guid.NewGuid(), PaymentService.Fingerprint(amount), CancellationToken.None)).Payment;
        public async ValueTask DisposeAsync()
        {
            await using var db = new WalletDbContext(Options());
            var payments = db.Payments.Where(p => p.WalletId == Wallet.Id).Select(p => p.Id);
            await db.PaymentReceipts.Where(r => payments.Contains(r.PaymentId)).ExecuteDeleteAsync();
            await db.Movements.Where(m => m.WalletId == Wallet.Id).ExecuteDeleteAsync();
            await db.Payments.Where(p => p.WalletId == Wallet.Id).ExecuteDeleteAsync();
            await db.Wallets.Where(w => w.Id == Wallet.Id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == User.Id).ExecuteDeleteAsync();
        }
    }
    [AzureSqlFact]
    public async Task Concurrent_starts_create_one_operation_and_reserve_one_external_attempt()
    {
        await using var f = await Fixture.Create(); var key = Guid.NewGuid(); var gate = new StartGate(6, "FROM [Wallets]");
        var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Store(gate).CreateAsync(f.User.Id, 1000m, key,
            PaymentService.Fingerprint(1000m), CancellationToken.None)));
        Assert.Equal(6, gate.Arrivals); Assert.Single(starts, x => x.Created);
        var id = starts[0].Payment.Id; Assert.All(starts, x => Assert.Equal(id, x.Payment.Id));
        var reserves = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Store().BeginCheckoutAsync(id, CancellationToken.None)));
        Assert.Single(reserves, x => x);
        await Assert.ThrowsAsync<PaymentRejectedException>(() => Store().CreateAsync(f.User.Id, 999m, key,
            PaymentService.Fingerprint(999m), CancellationToken.None));
        Assert.Equal(PaymentStatus.Unknown, (await Store().GetAsync(id, CancellationToken.None))!.Status);
    }
    [AzureSqlFact]
    public async Task Concurrent_notifications_and_later_replay_credit_once_even_after_new_context()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var result = PaymentTestData.Verified(p, id: RandomId());
        var gate = new StartGate(8, "FROM [Payments]");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Store(gate).ApplyAsync(result, CancellationToken.None)));
        Assert.Equal(8, gate.Arrivals);
        await Store().ApplyAsync(result with { EventKey = new string('A', 64) }, CancellationToken.None);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
        Assert.Single(await db.Movements.Where(m => m.PaymentId == p.Id).ToListAsync());
        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync(x => x.Id == p.Id)).Status);
    }
    [AzureSqlFact]
    public async Task Failure_before_commit_rolls_back_credit_state_movement_and_receipt_then_retry_recovers()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var result = PaymentTestData.Verified(p, id: RandomId());
        var fault = new FailCommit(); await Assert.ThrowsAsync<InjectedFailure>(() => Store(fault).ApplyAsync(result, CancellationToken.None));
        Assert.True(fault.Fired);
        await using (var db = new WalletDbContext(Options()))
        {
            Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
            Assert.Equal(PaymentStatus.Created, (await db.Payments.SingleAsync(x => x.Id == p.Id)).Status);
            Assert.False(await db.Movements.AnyAsync(m => m.PaymentId == p.Id)); Assert.False(await db.PaymentReceipts.AnyAsync(r => r.PaymentId == p.Id));
        }
        await Store().ApplyAsync(result, CancellationToken.None);
        Assert.NotNull((await Store().GetAsync(p.Id, CancellationToken.None))!.CreditedAt);
    }
    [AzureSqlFact]
    public async Task Cancellation_after_wallet_write_rolls_back_all_local_effects()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var result = PaymentTestData.Verified(p, id: RandomId());
        using var cancellation = new CancellationTokenSource(); var fault = new CancelWalletWrite(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(fault).ApplyAsync(result, cancellation.Token)); Assert.True(fault.Fired);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
        Assert.False(await db.Movements.AnyAsync(m => m.PaymentId == p.Id)); Assert.False(await db.PaymentReceipts.AnyAsync(r => r.PaymentId == p.Id));
        Assert.Null((await db.Payments.SingleAsync(x => x.Id == p.Id)).CreditedAt);
    }
    [AzureSqlFact]
    public async Task Lost_commit_response_can_be_retried_without_second_credit()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var result = PaymentTestData.Verified(p, id: RandomId());
        var fault = new FailAfterCommit(); await Assert.ThrowsAsync<InjectedFailure>(() => Store(fault).ApplyAsync(result, CancellationToken.None));
        Assert.True(fault.Fired); await Store().ApplyAsync(result, CancellationToken.None);
        await using var db = new WalletDbContext(Options());
        Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
        Assert.Equal(1, await db.Movements.CountAsync(m => m.PaymentId == p.Id));
    }
    [AzureSqlFact]
    public async Task Pending_rejected_and_duplicate_old_events_do_not_credit_or_downgrade_approval()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var id = RandomId();
        var pending = PaymentTestData.Verified(p, PaymentStatus.Pending, id, new string('1', 64));
        var rejected = PaymentTestData.Verified(p, PaymentStatus.Rejected, id, new string('2', 64));
        await Store().ApplyAsync(pending, CancellationToken.None); await Store().ApplyAsync(rejected, CancellationToken.None);
        await using (var db = new WalletDbContext(Options())) Assert.Equal(0m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
        var approved = PaymentTestData.Verified(p, PaymentStatus.Approved, RandomId(), new string('3', 64));
        await Store().ApplyAsync(approved, CancellationToken.None);
        await Store().ApplyAsync(approved with { Status = PaymentStatus.Pending, EventKey = new string('4', 64) }, CancellationToken.None);
        var final = (await Store().GetAsync(p.Id, CancellationToken.None))!;
        Assert.Equal(PaymentStatus.Approved, final.Status);
        await using var db2 = new WalletDbContext(Options()); Assert.Equal(1, await db2.Movements.CountAsync(m => m.PaymentId == p.Id));
    }
    [AzureSqlFact]
    public async Task One_external_payment_cannot_fund_two_intentions()
    {
        await using var f = await Fixture.Create(); var p1 = await f.Payment(); var p2 = await f.Payment(); var id = RandomId();
        await Store().ApplyAsync(PaymentTestData.Verified(p1, id: id), CancellationToken.None);
        await Assert.ThrowsAsync<PaymentRejectedException>(() => Store().ApplyAsync(PaymentTestData.Verified(p2, id: id,
            eventKey: new string('B', 64)), CancellationToken.None));
        await using var db = new WalletDbContext(Options());
        Assert.Equal(1000m, (await db.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
        Assert.False(await db.Movements.AnyAsync(m => m.PaymentId == p2.Id));
    }
    [AzureSqlFact]
    public async Task Mismatched_money_and_inactive_owner_do_not_change_balances()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var verified = PaymentTestData.Verified(p, id: RandomId());
        await Assert.ThrowsAsync<PaymentRejectedException>(() => Store().ApplyAsync(verified with { Amount = 1m }, CancellationToken.None));
        await using (var db = new WalletDbContext(Options()))
        { var user = await db.Users.SingleAsync(u => u.Id == f.User.Id); user.Disable(); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<PaymentRejectedException>(() => Store().ApplyAsync(verified, CancellationToken.None));
        await using var check = new WalletDbContext(Options()); Assert.Equal(0m, (await check.Wallets.SingleAsync(w => w.Id == f.Wallet.Id)).Balance);
    }
    [AzureSqlFact]
    public async Task Concurrency_retry_is_bounded_and_each_attempt_uses_a_fresh_context()
    {
        await using var f = await Fixture.Create(); var p = await f.Payment(); var fault = new FailConcurrency();
        Assert.Equal("CONCURRENCY_CONFLICT", (await Assert.ThrowsAsync<PaymentRejectedException>(() =>
            Store(fault).ApplyAsync(PaymentTestData.Verified(p, id: RandomId()), CancellationToken.None))).Code);
        Assert.Equal(3, fault.Contexts.Count); Assert.Equal(3, fault.Contexts.Distinct().Count());
    }
    [AzureSqlFact]
    public async Task Authenticated_http_creation_refresh_and_history_use_the_same_persisted_payment()
    {
        await using var f = await Fixture.Create();
        await using var identity = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        var gateway = new TestPaymentGateway();
        await using var app = identity.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPaymentGateway>(); s.AddSingleton<IPaymentGateway>(gateway);
            s.RemoveAll<PaymentSettings>(); s.AddSingleton(PaymentTestData.Settings());
        }));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", identity.Token(f.User));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var created = await client.PostAsJsonAsync("/api/payments", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var payment = (await created.Content.ReadFromJsonAsync<PaymentResponse>())!;
        gateway.Result = PaymentTestData.Verified((await Store().GetAsync(payment.Id, CancellationToken.None))!, id: RandomId());
        using var refreshed = await client.PostAsync($"/api/payments/{payment.Id}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("Approved", (await refreshed.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);
        using var repeated = await client.PostAsJsonAsync("/api/payments", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); Assert.Equal(1, gateway.Starts);
        var history = (await client.GetFromJsonAsync<MovementPage>("/api/wallets/me/movements"))!;
        var movement = Assert.Single(history.Items); Assert.Equal("Payment", movement.Source);
        Assert.Equal(payment.Id, movement.SourceId); Assert.Equal(1000m, movement.BalanceAfter);
        await using var db = new WalletDbContext(Options()); Assert.True(await new SqlUserRepository(db).IsReadyAsync(CancellationToken.None));
    }
    private static string RandomId() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, int.MaxValue).ToString();
    private sealed class StartGate(int participants, string match) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _seen; public int Arrivals => Math.Min(Volatile.Read(ref _seen), participants);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains(match, StringComparison.Ordinal))
            {
                var n = Interlocked.Increment(ref _seen); if (n == participants) _ready.TrySetResult();
                if (n <= participants) await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }
            return result;
        }
    }
    private sealed class InjectedFailure : Exception { }
    private sealed class FailCommit : DbTransactionInterceptor
    {
        public bool Fired;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { Fired = true; throw new InjectedFailure(); }
    }
    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        public bool Fired;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken ct = default)
        { Fired = true; throw new InjectedFailure(); }
    }
    private sealed class CancelWalletWrite(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public bool Fired;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken ct = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE [Wallets]", StringComparison.Ordinal)) { Fired = true; cancellation.Cancel(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailConcurrency : SaveChangesInterceptor
    {
        public List<DbContext> Contexts { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        { Contexts.Add(eventData.Context!); throw new DbUpdateConcurrencyException(); }
    }
}
