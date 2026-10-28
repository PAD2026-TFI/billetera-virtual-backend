using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;
public sealed class PaymentRecoveryTests
{
    [Fact]
    public async Task Search_with_rejected_attempts_recovers_the_approved_attempt_via_a_fresh_get()
    {
        var store = new TestPaymentStore(); var p = (await store.CreateAsync(Guid.NewGuid(), 1000, Guid.NewGuid(),
            PaymentService.Fingerprint(1000), CancellationToken.None)).Payment;
        var gateway = new TestPaymentGateway { Results = [PaymentTestData.Verified(p, PaymentStatus.Rejected, "1"), PaymentTestData.Verified(p, id: "2")] };
        await new PaymentProcessor(store, gateway, PaymentTestData.Settings()).ReconcileAsync(p, CancellationToken.None);
        Assert.Equal(1, gateway.Gets); Assert.Equal(1, store.Credits); Assert.Equal("2", p.ProviderPaymentId);
    }
    [Fact]
    public async Task Multiple_approved_payments_are_flagged_for_review_without_choosing_one_silently()
    {
        var store = new TestPaymentStore(); var p = (await store.CreateAsync(Guid.NewGuid(), 1000, Guid.NewGuid(),
            PaymentService.Fingerprint(1000), CancellationToken.None)).Payment;
        var gateway = new TestPaymentGateway { Results = [PaymentTestData.Verified(p, id: "1"), PaymentTestData.Verified(p, id: "2")] };
        await new PaymentProcessor(store, gateway, PaymentTestData.Settings()).ReconcileAsync(p, CancellationToken.None);
        Assert.True(p.RequiresReview); Assert.Equal("MULTIPLE_PROVIDER_PAYMENTS", p.LastIssue); Assert.Equal(0, store.Credits);
    }
    [Fact]
    public async Task Search_absence_keeps_the_intention_recoverable_and_never_creates_another_checkout()
    {
        var store = new TestPaymentStore(); var p = (await store.CreateAsync(Guid.NewGuid(), 1000, Guid.NewGuid(),
            PaymentService.Fingerprint(1000), CancellationToken.None)).Payment;
        p.BeginCheckout(DateTimeOffset.UtcNow); var gateway = new TestPaymentGateway();
        await new PaymentProcessor(store, gateway, PaymentTestData.Settings()).ReconcileAsync(p, CancellationToken.None);
        Assert.Equal(PaymentStatus.Unknown, p.Status); Assert.Equal(0, gateway.Starts); Assert.Equal(0, store.Credits);
    }
}
