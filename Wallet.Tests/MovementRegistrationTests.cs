using Wallet.Application;
using Wallet.Domain;
using Wallet.Domain.Entities;
using Xunit;
using DomainWallet = Wallet.Domain.Entities.Wallet;

namespace Wallet.Tests;

public sealed class MovementRegistrationTests
{
    private static (DomainWallet Source, DomainWallet Destination, Transfer Transfer) Completed()
    {
        var source = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destination = DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        source.Credit(100m); source.Debit(20m); destination.Credit(20m);
        var transfer = Transfer.Complete(source.Id, destination.Id, 20m, Guid.NewGuid(),
            TransferService.Fingerprint(destination.Id, 20m), DateTimeOffset.UtcNow);
        return (source, destination, transfer);
    }
    [Fact]
    public void Pair_records_exactly_one_debit_and_credit_with_matching_references_and_balances()
    {
        var (source, destination, transfer) = Completed();
        var movements = Movement.ForCompletedTransfer(transfer, source, destination);
        Assert.Equal(2, movements.Count); Assert.Equal(2, movements.Select(m => m.Id).Distinct().Count());
        var debit = Assert.Single(movements, m => m.Type == MovementType.Debit);
        var credit = Assert.Single(movements, m => m.Type == MovementType.Credit);
        Assert.Equal(source.Id, debit.WalletId); Assert.Equal(destination.Id, credit.WalletId);
        Assert.Equal(80m, debit.BalanceAfter); Assert.Equal(20m, credit.BalanceAfter);
        Assert.All(movements, m =>
        {
            Assert.Equal(transfer.Id, m.TransferId); Assert.Equal(transfer.Amount, m.Amount);
            Assert.Equal(transfer.CreatedAt, m.CreatedAt); Assert.Equal(MovementSource.Transfer, m.Source);
            Assert.Null(m.DevelopmentFundingId);
        });
        Assert.Equal(80m, source.Balance); Assert.Equal(20m, destination.Balance);
    }
    [Fact]
    public void Unrelated_or_reversed_wallets_cannot_generate_transfer_movements()
    {
        var (source, destination, transfer) = Completed();
        Assert.Throws<ArgumentException>(() => Movement.ForCompletedTransfer(transfer, destination, source));
        Assert.Throws<ArgumentException>(() => Movement.ForCompletedTransfer(transfer, source,
            DomainWallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow)));
    }
    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("19.99")]
    [InlineData("20.001")]
    public void Impossible_credit_balance_is_rejected(string amount)
    {
        var (_, _, transfer) = Completed();
        Assert.Throws<ArgumentException>(() => Movement.ForTransfer(transfer, false,
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
