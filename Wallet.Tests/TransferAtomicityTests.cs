using Wallet.Application;
using Wallet.Domain;
using Wallet.Domain.Entities.Status;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class TransferAtomicityTests
{
    private static Transfer Create(DomainWallet source, DomainWallet destination, decimal amount) =>
        Transfer.Complete(source.Id, destination.Id, amount, Guid.NewGuid(),
            TransferService.Fingerprint(destination.Id, amount), DateTimeOffset.UtcNow);
    [Fact]
    public void Both_balances_are_updated_and_total_is_conserved()
    {
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(100m); destination.Credit(50m);
        Create(source, destination, 20m).ApplyTo(source, destination);
        Assert.Equal(80m, source.Balance); Assert.Equal(70m, destination.Balance);
        Assert.Equal(150m, source.Balance + destination.Balance);
    }
    [Fact]
    public void Destination_overflow_cannot_leave_a_partial_debit_in_memory()
    {
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(100m); destination.Credit(MoneyRules.MaxBalance);
        Assert.Throws<ArgumentException>(() => Create(source, destination, 20m).ApplyTo(source, destination));
        Assert.Equal(100m, source.Balance); Assert.Equal(MoneyRules.MaxBalance, destination.Balance);
    }
    [Fact]
    public void Insufficient_funds_do_not_credit_destination()
    {
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(10m);
        Assert.Throws<ArgumentException>(() => Create(source, destination, 20m).ApplyTo(source, destination));
        Assert.Equal(10m, source.Balance); Assert.Equal(0m, destination.Balance);
    }
    [Fact]
    public void Unrelated_wallet_does_not_change_either_balance()
    {
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var unrelated = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(100m);
        Assert.Throws<ArgumentException>(() => Create(source, destination, 20m).ApplyTo(source, unrelated));
        Assert.Equal(100m, source.Balance); Assert.Equal(0m, unrelated.Balance);
    }
}
