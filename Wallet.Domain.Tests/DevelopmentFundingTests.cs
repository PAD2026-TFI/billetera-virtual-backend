using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class DevelopmentFundingTests
{
    [Fact]
    public void Create_initializes_funding_properly()
    {
        var walletId = Guid.NewGuid();
        var amount = Money.From(1000m);
        var key = Guid.NewGuid();
        var hash = new string('1', 64);
        var now = DateTimeOffset.UtcNow;

        var funding = DevelopmentFunding.Create(walletId, amount, key, hash, now);

        Assert.NotEqual(Guid.Empty, funding.Id);
        Assert.Equal(walletId, funding.WalletId);
        Assert.Equal(amount, funding.Amount);
        Assert.Equal(key, funding.IdempotencyKey);
        Assert.Equal(hash, funding.RequestHash);
        Assert.Equal(now.ToUniversalTime(), funding.CreatedAt);
    }

    [Fact]
    public void Create_with_invalid_hash_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() =>
            DevelopmentFunding.Create(Guid.NewGuid(), 100m, Guid.NewGuid(), "short", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MatchesRequest_validates_match()
    {
        var walletId = Guid.NewGuid();
        var amount = Money.From(500m);
        var hash = new string('2', 64);

        var funding = DevelopmentFunding.Create(walletId, amount, Guid.NewGuid(), hash, DateTimeOffset.UtcNow);

        Assert.True(funding.MatchesRequest(walletId, amount, hash.ToUpperInvariant()));
        Assert.False(funding.MatchesRequest(Guid.NewGuid(), amount, hash));
        Assert.False(funding.MatchesRequest(walletId, Money.From(501m), hash));
    }
}
