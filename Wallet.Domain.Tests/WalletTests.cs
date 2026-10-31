using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class WalletTests
{
    [Fact]
    public void Create_initializes_wallet_with_zero_balance_and_specified_owner()
    {
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var wallet = Domain.Wallet.Create(ownerId, now);

        Assert.NotEqual(Guid.Empty, wallet.Id);
        Assert.Equal(ownerId, wallet.OwnerId);
        Assert.Equal(ownerId, wallet.UserId);
        Assert.Equal(Money.Zero("ARS"), wallet.Balance);
        Assert.Equal("ARS", wallet.Currency);
        Assert.Equal(now.ToUniversalTime(), wallet.CreatedAt);
    }

    [Fact]
    public void Create_with_empty_owner_id_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() =>
            Domain.Wallet.Create(Guid.Empty, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Credit_with_valid_Money_increments_balance()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var credit = Money.From(250.50m);

        wallet.Credit(credit);

        Assert.Equal(Money.From(250.50m), wallet.Balance);
    }

    [Fact]
    public void Credit_with_valid_decimal_increments_balance()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        wallet.Credit(100.25m);

        Assert.Equal(Money.From(100.25m), wallet.Balance);
    }

    [Fact]
    public void Credit_with_zero_or_negative_amount_throws_argument_exception()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => wallet.Credit(0m));
        Assert.Throws<ArgumentException>(() => wallet.Credit(-10m));
    }

    [Fact]
    public void Debit_with_sufficient_funds_decrements_balance()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(500m);

        wallet.Debit(Money.From(150.25m));

        Assert.Equal(Money.From(349.75m), wallet.Balance);
    }

    [Fact]
    public void Debit_with_decimal_decrements_balance()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(500m);

        wallet.Debit(200m);

        Assert.Equal(Money.From(300m), wallet.Balance);
    }

    [Fact]
    public void Debit_exceeding_current_balance_throws_invalid_operation_exception()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(100m);

        var ex = Assert.Throws<InvalidOperationException>(() => wallet.Debit(Money.From(100.01m)));
        Assert.Contains("Insufficient funds", ex.Message);
    }

    [Fact]
    public void Debit_with_zero_or_negative_amount_throws_argument_exception()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(100m);

        Assert.Throws<ArgumentException>(() => wallet.Debit(0m));
        Assert.Throws<ArgumentException>(() => wallet.Debit(-50m));
    }
}
