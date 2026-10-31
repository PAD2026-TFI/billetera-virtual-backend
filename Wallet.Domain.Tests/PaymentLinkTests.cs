using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class PaymentLinkTests
{
    private readonly Guid _destinationWalletId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_with_valid_parameters_initializes_active_link()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            1500.50m,
            "Cena para 2",
            "Mesa 4",
            PaymentLinkType.SingleUse,
            _now,
            ttl: TimeSpan.FromMinutes(30));

        Assert.NotEqual(Guid.Empty, link.Id);
        Assert.Equal(_destinationWalletId, link.DestinationWalletId);
        Assert.Equal(1500.50m, link.Amount.Amount);
        Assert.Equal("ARS", link.Currency);
        Assert.Equal("Cena para 2", link.Title);
        Assert.Equal("Mesa 4", link.Description);
        Assert.Equal(PaymentLinkStatus.Active, link.Status);
        Assert.Equal(PaymentLinkType.SingleUse, link.Type);
        Assert.False(string.IsNullOrWhiteSpace(link.Token));
        Assert.Equal(_now, link.CreatedAt);
        Assert.Equal(_now.AddMinutes(30), link.ExpiresAt);
    }

    [Fact]
    public void Create_with_empty_destination_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() => PaymentLink.Create(
            Guid.Empty,
            100m,
            "Concepto",
            null,
            PaymentLinkType.SingleUse,
            _now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_with_invalid_title_throws_argument_exception(string? title)
    {
        Assert.Throws<ArgumentException>(() => PaymentLink.Create(
            _destinationWalletId,
            100m,
            title!,
            null,
            PaymentLinkType.SingleUse,
            _now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_with_non_positive_amount_throws_argument_exception(decimal amount)
    {
        Assert.Throws<ArgumentException>(() => PaymentLink.Create(
            _destinationWalletId,
            amount,
            "Concepto",
            null,
            PaymentLinkType.SingleUse,
            _now));
    }

    [Fact]
    public void MarkCompleted_single_use_changes_status_to_completed()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            250m,
            "Cafe",
            null,
            PaymentLinkType.SingleUse,
            _now);

        link.MarkCompleted(_now.AddMinutes(5));

        Assert.Equal(PaymentLinkStatus.Completed, link.Status);
    }

    [Fact]
    public void MarkCompleted_multi_use_keeps_status_active()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            500m,
            "Entrada General",
            null,
            PaymentLinkType.MultiUse,
            _now);

        link.MarkCompleted(_now.AddMinutes(5));

        Assert.Equal(PaymentLinkStatus.Active, link.Status);
    }

    [Fact]
    public void MarkCompleted_when_already_completed_throws_invalid_operation()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            250m,
            "Cafe",
            null,
            PaymentLinkType.SingleUse,
            _now);

        link.MarkCompleted(_now.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() => link.MarkCompleted(_now.AddMinutes(6)));
    }

    [Fact]
    public void EnsureIsPayable_when_expired_by_time_transitions_to_expired_and_throws()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            100m,
            "Promo",
            null,
            PaymentLinkType.SingleUse,
            _now,
            ttl: TimeSpan.FromMinutes(10));

        Assert.Throws<InvalidOperationException>(() => link.EnsureIsPayable(_now.AddMinutes(11)));
        Assert.Equal(PaymentLinkStatus.Expired, link.Status);
    }

    [Fact]
    public void Cancel_transitions_status_to_cancelled()
    {
        var link = PaymentLink.Create(
            _destinationWalletId,
            100m,
            "Pedido",
            null,
            PaymentLinkType.SingleUse,
            _now);

        link.Cancel(_now.AddMinutes(2));

        Assert.Equal(PaymentLinkStatus.Cancelled, link.Status);
        Assert.Throws<InvalidOperationException>(() => link.EnsureIsPayable(_now.AddMinutes(3)));
    }
}
