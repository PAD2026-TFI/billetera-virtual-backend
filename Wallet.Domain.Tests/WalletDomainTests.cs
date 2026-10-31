
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class WalletDomainTests
{
    [Fact]
    public void Create_with_valid_owner_initializes_zero_balance_wallet_in_utc()
    {
        var ownerId = Guid.NewGuid();
        var localTime = new DateTimeOffset(2026, 10, 30, 15, 30, 0, TimeSpan.FromHours(-3));

        var wallet = Wallet.Domain.Wallet.Create(ownerId, localTime);

        Assert.NotEqual(Guid.Empty, wallet.Id);
        Assert.Equal(ownerId, wallet.OwnerId);
        Assert.Equal(0m, wallet.Balance.Amount);
        Assert.Equal("ARS", wallet.Balance.Currency);
        Assert.Equal("ARS", wallet.Currency);
        Assert.Equal(localTime.ToUniversalTime(), wallet.CreatedAt);
        Assert.Equal(TimeSpan.Zero, wallet.CreatedAt.Offset);
    }

    [Fact]
    public void Create_with_empty_owner_id_throws_argument_exception()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.ThrowsAny<ArgumentException>(() => Wallet.Domain.Wallet.Create(Guid.Empty, now));
    }

    [Fact]
    public void Credit_with_valid_money_increases_balance()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var creditAmount = Money.From(1500.50m);

        wallet.Credit(creditAmount);

        Assert.Equal(1500.50m, wallet.Balance.Amount);
        Assert.Equal("ARS", wallet.Balance.Currency);
    }

    [Fact]
    public void Credit_with_decimal_overload_increases_balance()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        wallet.Credit(250.75m);

        Assert.Equal(250.75m, wallet.Balance.Amount);
        Assert.Equal("ARS", wallet.Currency);
    }

    [Fact]
    public void Credit_is_additive_over_multiple_operations()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        wallet.Credit(100m);
        wallet.Credit(50.25m);
        wallet.Credit(Money.From(49.75m));

        Assert.Equal(200.00m, wallet.Balance.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Credit_with_zero_or_negative_decimal_throws_argument_exception(decimal invalidAmount)
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => wallet.Credit(invalidAmount));
        Assert.Equal(0m, wallet.Balance.Amount);
    }

    [Fact]
    public void Credit_with_zero_money_instance_throws_argument_exception()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.ThrowsAny<ArgumentException>(() => wallet.Credit(Money.Zero()));
        Assert.Equal(0m, wallet.Balance.Amount);
    }

    [Fact]
    public void Credit_exceeding_maximum_operational_limit_throws_overflow_exception()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(Money.MaxAllowedAmount);

        Assert.Throws<ArgumentException>(() => wallet.Credit(0.01m));
        Assert.Equal(Money.MaxAllowedAmount, wallet.Balance.Amount);
    }

    [Fact]
    public void Debit_with_sufficient_balance_decreases_balance()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(500m);

        wallet.Debit(Money.From(175.25m));

        Assert.Equal(324.75m, wallet.Balance.Amount);
    }

    [Fact]
    public void Debit_with_decimal_overload_decreases_balance()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(1000m);

        wallet.Debit(250m);

        Assert.Equal(750m, wallet.Balance.Amount);
    }

    [Fact]
    public void Debit_exact_total_balance_leaves_balance_at_zero()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(300m);

        wallet.Debit(300m);

        Assert.Equal(0m, wallet.Balance.Amount);
        Assert.Equal(Money.Zero(), wallet.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-0.01)]
    public void Debit_with_zero_or_negative_amount_throws_argument_exception(decimal invalidAmount)
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(100m);

        Assert.ThrowsAny<ArgumentException>(() => wallet.Debit(invalidAmount));
        Assert.Equal(100m, wallet.Balance.Amount);
    }

    [Fact]
    public void Debit_exceeding_current_balance_throws_invalid_operation_exception()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        wallet.Credit(100m);

        var ex = Assert.Throws<InvalidOperationException>(() => wallet.Debit(100.01m));

        Assert.Contains("Insufficient funds", ex.Message);
        Assert.Equal(100m, wallet.Balance.Amount);
    }

    [Fact]
    public void Consecutive_credits_and_debits_maintain_exact_arithmetic_consistency()
    {
        var wallet = Wallet.Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        wallet.Credit(1000m);
        wallet.Debit(250.50m);
        wallet.Credit(100.25m);
        wallet.Debit(49.75m);

        Assert.Equal(800.00m, wallet.Balance.Amount);
    }
}