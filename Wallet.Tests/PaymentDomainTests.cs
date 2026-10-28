using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;
public sealed class PaymentDomainTests
{
    private static Payment New(decimal amount = 1000m) => Payment.Create(Guid.NewGuid(), amount,
        Guid.NewGuid(), PaymentService.Fingerprint(amount), DateTimeOffset.UtcNow);
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.001)]
    public void Invalid_money_cannot_create_a_payment(decimal amount) => Assert.Throws<ArgumentException>(() => New(amount));
    [Fact]
    public void Checkout_is_reserved_once_and_response_cannot_downgrade_a_concurrent_approval()
    {
        var payment = New(); var now = DateTimeOffset.UtcNow;
        Assert.True(payment.BeginCheckout(now)); Assert.False(payment.BeginCheckout(now));
        Assert.Equal(PaymentStatus.Unknown, payment.Status);
        Assert.True(payment.ApplyVerified("123", PaymentStatus.Approved, now));
        payment.AttachCheckout("preference", "https://sandbox.mercadopago.com.ar/checkout/test", now);
        Assert.Equal(PaymentStatus.Approved, payment.Status); Assert.NotNull(payment.CreditedAt);
    }
    [Fact]
    public void Rejected_attempt_does_not_block_another_approved_attempt_but_cannot_credit_twice()
    {
        var payment = New(); var now = DateTimeOffset.UtcNow;
        Assert.False(payment.ApplyVerified("1", PaymentStatus.Rejected, now));
        Assert.True(payment.ApplyVerified("2", PaymentStatus.Approved, now));
        Assert.False(payment.ApplyVerified("2", PaymentStatus.Approved, now));
        Assert.Throws<InvalidOperationException>(() => payment.ApplyVerified("3", PaymentStatus.Approved, now));
        Assert.Equal("2", payment.ProviderPaymentId);
        Assert.False(payment.ApplyVerified("2", PaymentStatus.Pending, now));
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.False(payment.ApplyVerified("1", PaymentStatus.Rejected, now));
        Assert.Equal("2", payment.ProviderPaymentId);
    }
    [Fact]
    public void Payment_movement_is_a_credit_with_reference_and_exact_balance()
    {
        var payment = New(); var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => Movement.ForPayment(payment, 1000m, now));
        payment.ApplyVerified("123", PaymentStatus.Approved, now);
        var movement = Movement.ForPayment(payment, 1500m, now);
        Assert.Equal(MovementSource.Payment, movement.Source); Assert.Equal(MovementType.Credit, movement.Type);
        Assert.Equal(payment.Id, movement.PaymentId); Assert.Equal(payment.WalletId, movement.WalletId);
        Assert.Equal(1500m, movement.BalanceAfter); Assert.Null(movement.TransferId); Assert.Null(movement.DevelopmentFundingId);
        Assert.Throws<ArgumentException>(() => Movement.ForPayment(payment, 999m, now));
    }
    [Fact]
    public void Recovery_is_bounded_and_exposes_review_instead_of_silently_forgetting_operations()
    {
        var payment = New(); var now = DateTimeOffset.UtcNow; payment.BeginCheckout(now);
        for (var i = 0; i < 10; i++) payment.RecordIssue("PROVIDER_TIMEOUT", now);
        Assert.True(payment.RequiresReview); Assert.Equal(PaymentStatus.Unknown, payment.Status);
        Assert.Null(payment.CreditedAt); Assert.True(payment.NextCheckAt > now);
    }
    [Fact]
    public void Fingerprint_is_independent_of_current_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("es-AR"); var first = PaymentService.Fingerprint(12.30m);
            System.Globalization.CultureInfo.CurrentCulture = new("en-US"); Assert.Equal(first, PaymentService.Fingerprint(12.3m));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
    [Fact]
    public void Enabled_settings_require_external_secrets_and_a_safe_public_origin()
    {
        new PaymentSettings().Validate();
        Assert.Throws<InvalidOperationException>(() => new PaymentSettings { Enabled = true }.Validate());
        var valid = PaymentTestData.Settings(); valid.Validate();
        valid.PublicBaseUrl = "http://example.com"; Assert.Throws<InvalidOperationException>(valid.Validate);
    }
    [Fact]
    public void Older_rejected_attempt_cannot_hide_a_more_recent_pending_attempt()
    {
        var p = New(); var now = DateTimeOffset.UtcNow;
        p.ApplyVerified("1", PaymentStatus.Rejected, now, now.AddMinutes(-2));
        p.ApplyVerified("2", PaymentStatus.Pending, now, now.AddMinutes(-1));
        Assert.Equal(PaymentStatus.Pending, p.Status);
        p.ApplyVerified("1", PaymentStatus.Rejected, now, now.AddMinutes(-2));
        Assert.Equal(PaymentStatus.Pending, p.Status); Assert.Equal("2", p.ProviderPaymentId);
        Assert.True(p.ApplyVerified("2", PaymentStatus.Approved, now, now));
    }

}
