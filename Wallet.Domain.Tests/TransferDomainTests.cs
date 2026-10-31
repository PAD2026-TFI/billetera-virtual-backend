using System.Globalization;
using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class TransferDomainTests
{
    private static readonly Guid ValidSourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ValidDestinationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ValidKey = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string ValidHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    [Fact]
    public void Complete_with_valid_arguments_initializes_transfer_in_completed_status()
    {
        var now = new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.FromHours(-3));
        var amount = Money.From(500m);

        var transfer = Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            amount,
            ValidKey,
            ValidHash,
            now);

        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.Equal(ValidSourceId, transfer.SourceWalletId);
        Assert.Equal(ValidDestinationId, transfer.DestinationWalletId);
        Assert.Equal(amount, transfer.Amount);
        Assert.Equal("ARS", transfer.Currency);
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal(ValidKey, transfer.IdempotencyKey);
        Assert.Equal(ValidHash, transfer.RequestHash);
        Assert.Equal(now.ToUniversalTime(), transfer.CreatedAt);
        Assert.Equal(TimeSpan.Zero, transfer.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("100.50")]
    [InlineData("780400.00")]
    public void Complete_with_boundary_amounts_succeeds(string amountStr)
    {
        var amount = decimal.Parse(amountStr, CultureInfo.InvariantCulture);

        var transfer = Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            amount,
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow);

        Assert.Equal(amount, transfer.Amount.Amount);
        Assert.Equal("ARS", transfer.Currency);
    }

    [Theory]
    [InlineData("780400.01")]
    [InlineData("1000000.00")]
    public void Complete_with_amount_exceeding_threshold_throws_argument_exception(string amountStr)
    {
        var amount = decimal.Parse(amountStr, CultureInfo.InvariantCulture);

        var ex = Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            amount,
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow));

        Assert.Contains("exceeds maximum allowed threshold", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Complete_with_zero_or_negative_amount_throws_argument_exception(decimal invalidAmount)
    {
        Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            invalidAmount,
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_with_empty_or_identical_wallet_ids_throws_argument_exception()
    {
        var now = DateTimeOffset.UtcNow;
        var amount = Money.From(100m);

        // Origen vacío
        Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            Guid.Empty, ValidDestinationId, amount, ValidKey, ValidHash, now));

        // Destinatario vacío
        Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId, Guid.Empty, amount, ValidKey, ValidHash, now));

        // source = destination
        var ex = Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId, ValidSourceId, amount, ValidKey, ValidHash, now));

        Assert.Contains("must be distinct", ex.Message);
    }

    [Fact]
    public void Complete_with_empty_idempotency_key_throws_argument_exception()
    {
        Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            Money.From(100m),
            Guid.Empty,
            ValidHash,
            DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hex-string")]
    [InlineData("a1b2c3d4")] // muy corto
    public void Complete_with_invalid_request_hash_throws_argument_exception(string? invalidHash)
    {
        Assert.ThrowsAny<ArgumentException>(() => Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            Money.From(100m),
            ValidKey,
            invalidHash!,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ApplyTo_with_sufficient_balance_updates_both_wallets_and_conserves_total_funds()
    {
        var sourceWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destinationWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        sourceWallet.Credit(1000m);
        destinationWallet.Credit(250m);

        var initialTotal = sourceWallet.Balance + destinationWallet.Balance;
        var transferAmount = Money.From(300m);

        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destinationWallet.Id,
            transferAmount,
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow);

        transfer.ApplyTo(sourceWallet, destinationWallet);

        Assert.Equal(700m, sourceWallet.Balance.Amount);
        Assert.Equal(550m, destinationWallet.Balance.Amount);
        Assert.Equal(initialTotal, sourceWallet.Balance + destinationWallet.Balance);
    }

    [Fact]
    public void ApplyTo_with_insufficient_source_balance_throws_and_preserves_both_wallets()
    {
        var sourceWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destinationWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        sourceWallet.Credit(150m);
        destinationWallet.Credit(50m);

        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destinationWallet.Id,
            Money.From(200m),
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => transfer.ApplyTo(sourceWallet, destinationWallet));

        // El balance no se debería modificar
        Assert.Equal(150m, sourceWallet.Balance.Amount);
        Assert.Equal(50m, destinationWallet.Balance.Amount);
    }

    [Fact]
    public void ApplyTo_with_destination_overflow_throws_and_preserves_both_wallets()
    {
        var sourceWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destinationWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        sourceWallet.Credit(1000m);
        destinationWallet.Credit(Money.MaxAllowedAmount);

        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destinationWallet.Id,
            Money.From(100m),
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow);

        Assert.Throws<OverflowException>(() => transfer.ApplyTo(sourceWallet, destinationWallet));

        // El balance de la billetera origen no debería sufrir un débito
        Assert.Equal(1000m, sourceWallet.Balance.Amount);
        Assert.Equal(Money.MaxAllowedAmount, destinationWallet.Balance.Amount);
    }

    [Fact]
    public void ApplyTo_with_unmatched_wallets_throws_argument_exception()
    {
        var sourceWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destinationWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var unrelatedWallet = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        sourceWallet.Credit(500m);

        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destinationWallet.Id,
            Money.From(100m),
            ValidKey,
            ValidHash,
            DateTimeOffset.UtcNow);

        
        Assert.ThrowsAny<ArgumentException>(() => transfer.ApplyTo(sourceWallet, unrelatedWallet));

        
        Assert.ThrowsAny<ArgumentException>(() => transfer.ApplyTo(destinationWallet, sourceWallet));

        Assert.Equal(500m, sourceWallet.Balance.Amount);
        Assert.Equal(0m, destinationWallet.Balance.Amount);
    }

    [Fact]
    public void MatchesRequest_evaluates_destination_amount_and_hash_correctly()
    {
        var transfer = Transfer.Complete(
            ValidSourceId,
            ValidDestinationId,
            Money.From(300m),
            ValidKey,
            ValidHash.ToUpperInvariant(),
            DateTimeOffset.UtcNow);

        
        Assert.True(transfer.MatchesRequest(ValidDestinationId, 300m, ValidHash.ToLowerInvariant()));

        Assert.True(transfer.MatchesRequest(ValidDestinationId, Money.From(300m), ValidHash));

        Assert.False(transfer.MatchesRequest(Guid.NewGuid(), 300m, ValidHash));
        
        Assert.False(transfer.MatchesRequest(ValidDestinationId, 300.01m, ValidHash));
        
        Assert.False(transfer.MatchesRequest(ValidDestinationId, 300m, new string('0', 64)));
    }
}