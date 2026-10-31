using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class PaymentTests
{
    private readonly Guid _linkId = Guid.NewGuid();
    private readonly Guid _merchantId = Guid.NewGuid();
    private readonly Guid _destWallet = Guid.NewGuid();
    private readonly Guid _payerUser = Guid.NewGuid();
    private readonly Guid _payerWallet = Guid.NewGuid();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public void CreateForWallet_sets_initial_properties_correctly()
    {
        var amount = Money.Positive(1500m);
        var payment = Payment.CreateForWallet(
            _linkId,
            _merchantId,
            _destWallet,
            _payerUser,
            _payerWallet,
            amount,
            "idemp-123",
            "hash-abc",
            _now);

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(_linkId, payment.PaymentLinkId);
        Assert.Equal(_merchantId, payment.MerchantOwnerId);
        Assert.Equal(_destWallet, payment.DestinationWalletId);
        Assert.Equal(_payerUser, payment.PayerUserId);
        Assert.Equal(_payerWallet, payment.PayerWalletId);
        Assert.Equal(1500m, payment.Amount.Amount);
        Assert.Equal("ARS", payment.Currency);
        Assert.Equal(PaymentMethod.Wallet, payment.Method);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal("idemp-123", payment.IdempotencyKey);
        Assert.Equal("hash-abc", payment.RequestHash);
        Assert.Null(payment.CompletedAt);
    }

    [Fact]
    public void CreateForWallet_with_same_source_and_destination_wallet_throws()
    {
        var amount = Money.Positive(100m);
        Assert.Throws<ArgumentException>(() =>
            Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _destWallet, amount, null, null, _now));
    }

    [Fact]
    public void CreateForCard_sets_card_properties_correctly()
    {
        var amount = Money.Positive(300m);
        var payment = Payment.CreateForCard(
            _linkId,
            _merchantId,
            _destWallet,
            amount,
            "Visa",
            "0366",
            "Juan Perez",
            null,
            "idemp-456",
            "hash-def",
            _now);

        Assert.Equal(PaymentMethod.Card, payment.Method);
        Assert.Equal("Visa", payment.CardBrand);
        Assert.Equal("0366", payment.CardLast4);
        Assert.Equal("Juan Perez", payment.CardHolderName);
        Assert.Null(payment.PayerUserId);
        Assert.Null(payment.PayerWalletId);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    [Fact]
    public void CreateForCard_with_invalid_last4_throws()
    {
        var amount = Money.Positive(100m);
        Assert.Throws<ArgumentException>(() =>
            Payment.CreateForCard(_linkId, _merchantId, _destWallet, amount, "Visa", "12", "Holder", null, null, null, _now));

        Assert.Throws<ArgumentException>(() =>
            Payment.CreateForCard(_linkId, _merchantId, _destWallet, amount, "Visa", "abcd", "Holder", null, null, null, _now));
    }

    [Fact]
    public void MarkSucceeded_transitions_to_succeeded_with_audit_info()
    {
        var payment = Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _payerWallet, Money.Positive(500m), null, null, _now);
        var transferId = Guid.NewGuid();
        var completedAt = _now.AddMinutes(1);

        payment.MarkSucceeded(transferId, "AUTH-123", completedAt);

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(transferId, payment.TransferId);
        Assert.Equal("AUTH-123", payment.AuthorizationCode);
        Assert.Equal(completedAt.ToUniversalTime(), payment.CompletedAt);
    }

    [Fact]
    public void MarkFailed_transitions_to_failed_with_reason()
    {
        var payment = Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _payerWallet, Money.Positive(500m), null, null, _now);
        var completedAt = _now.AddMinutes(1);

        payment.MarkFailed("INSUFFICIENT_FUNDS", completedAt);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("INSUFFICIENT_FUNDS", payment.FailureReason);
        Assert.Equal(completedAt.ToUniversalTime(), payment.CompletedAt);
    }

    [Fact]
    public void MarkSucceeded_from_succeeded_status_throws()
    {
        var payment = Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _payerWallet, Money.Positive(500m), null, null, _now);
        payment.MarkSucceeded(Guid.NewGuid(), "AUTH", _now);

        Assert.Throws<InvalidOperationException>(() =>
            payment.MarkSucceeded(Guid.NewGuid(), "AUTH2", _now));
    }

    [Fact]
    public void Cancel_transitions_to_cancelled()
    {
        var payment = Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _payerWallet, Money.Positive(500m), null, null, _now);
        payment.Cancel(_now);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void Cancel_already_succeeded_payment_throws()
    {
        var payment = Payment.CreateForWallet(_linkId, _merchantId, _destWallet, _payerUser, _payerWallet, Money.Positive(500m), null, null, _now);
        payment.MarkSucceeded(Guid.NewGuid(), "AUTH", _now);

        Assert.Throws<InvalidOperationException>(() => payment.Cancel(_now));
    }
}
