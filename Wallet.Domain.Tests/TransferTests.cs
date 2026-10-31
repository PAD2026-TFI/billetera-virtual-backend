using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class TransferTests
{
    [Fact]
    public void Complete_creates_completed_transfer_with_valid_parameters()
    {
        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        var amount = Money.From(5000m);
        var key = Guid.NewGuid();
        var hash = new string('a', 64);
        var now = DateTimeOffset.UtcNow;

        var transfer = Transfer.Complete(sourceId, destId, amount, key, hash, now);

        Assert.NotEqual(Guid.Empty, transfer.Id);
        Assert.Equal(sourceId, transfer.SourceWalletId);
        Assert.Equal(destId, transfer.DestinationWalletId);
        Assert.Equal(amount, transfer.Amount);
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.Equal(key, transfer.IdempotencyKey);
        Assert.Equal(hash, transfer.RequestHash);
        Assert.Equal(now.ToUniversalTime(), transfer.CreatedAt);
    }

    [Fact]
    public void Complete_with_same_source_and_destination_throws_argument_exception()
    {
        var sameId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            Transfer.Complete(sameId, sameId, Money.From(100m), Guid.NewGuid(), new string('a', 64), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_with_amount_exceeding_threshold_throws_argument_exception()
    {
        var excessive = Transfer.MaximumTransferAmount.Amount + 0.01m;

        Assert.Throws<ArgumentException>(() =>
            Transfer.Complete(Guid.NewGuid(), Guid.NewGuid(), excessive, Guid.NewGuid(), new string('a', 64), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_with_invalid_hash_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() =>
            Transfer.Complete(Guid.NewGuid(), Guid.NewGuid(), Money.From(100m), Guid.NewGuid(), "invalid-hash", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ApplyTo_modifies_both_wallets_correctly()
    {
        var source = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var dest = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        source.Credit(1000m);
        dest.Credit(100m);

        var transfer = Transfer.Complete(source.Id, dest.Id, Money.From(400m), Guid.NewGuid(), new string('b', 64), DateTimeOffset.UtcNow);

        transfer.ApplyTo(source, dest);

        Assert.Equal(Money.From(600m), source.Balance);
        Assert.Equal(Money.From(500m), dest.Balance);
    }

    [Fact]
    public void ApplyTo_with_insufficient_funds_in_source_throws_invalid_operation_exception()
    {
        var source = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var dest = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        source.Credit(50m);

        var transfer = Transfer.Complete(source.Id, dest.Id, Money.From(100m), Guid.NewGuid(), new string('c', 64), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => transfer.ApplyTo(source, dest));
    }

    [Fact]
    public void MatchesRequest_verifies_destination_amount_and_hash_case_insensitively()
    {
        var sourceId = Guid.NewGuid();
        var destId = Guid.NewGuid();
        var amount = Money.From(200m);
        var hash = new string('c', 64);

        var transfer = Transfer.Complete(sourceId, destId, amount, Guid.NewGuid(), hash, DateTimeOffset.UtcNow);

        Assert.True(transfer.MatchesRequest(destId, amount, hash.ToUpperInvariant()));
        Assert.False(transfer.MatchesRequest(Guid.NewGuid(), amount, hash));
        Assert.False(transfer.MatchesRequest(destId, Money.From(201m), hash));
        Assert.False(transfer.MatchesRequest(destId, amount, new string('d', 64)));
    }
}
