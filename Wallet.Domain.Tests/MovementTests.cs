using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class MovementTests
{
    [Fact]
    public void ForCompletedTransfer_creates_debit_and_credit_movements_with_correct_ledger_balances()
    {
        var sourceWallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destWallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        sourceWallet.Credit(1000m);
        destWallet.Credit(200m);

        var amount = Money.From(300m);
        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destWallet.Id,
            amount,
            Guid.NewGuid(),
            new string('f', 64),
            DateTimeOffset.UtcNow);

        transfer.ApplyTo(sourceWallet, destWallet);

        var movements = Movement.ForCompletedTransfer(transfer, sourceWallet, destWallet);

        Assert.Equal(2, movements.Count);

        var debit = movements[0];
        Assert.Equal(sourceWallet.Id, debit.WalletId);
        Assert.Equal(MovementType.Debit, debit.Type);
        Assert.Equal(amount, debit.Amount);
        Assert.Equal(Money.From(700m), debit.BalanceAfter);
        Assert.Equal(MovementSource.Transfer, debit.Source);
        Assert.Equal(transfer.Id, debit.TransferId);
        Assert.Null(debit.DevelopmentFundingId);

        var credit = movements[1];
        Assert.Equal(destWallet.Id, credit.WalletId);
        Assert.Equal(MovementType.Credit, credit.Type);
        Assert.Equal(amount, credit.Amount);
        Assert.Equal(Money.From(500m), credit.BalanceAfter);
        Assert.Equal(MovementSource.Transfer, credit.Source);
        Assert.Equal(transfer.Id, credit.TransferId);
        Assert.Null(credit.DevelopmentFundingId);
    }

    [Fact]
    public void ForDevelopmentFunding_creates_credit_movement_referencing_funding_id()
    {
        var wallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var funding = DevelopmentFunding.Create(
            wallet.Id,
            Money.From(500m),
            Guid.NewGuid(),
            new string('e', 64),
            DateTimeOffset.UtcNow);

        wallet.Credit(funding.Amount);

        var movement = Movement.ForDevelopmentFunding(funding, wallet.Balance);

        Assert.Equal(wallet.Id, movement.WalletId);
        Assert.Equal(MovementType.Credit, movement.Type);
        Assert.Equal(funding.Amount, movement.Amount);
        Assert.Equal(wallet.Balance, movement.BalanceAfter);
        Assert.Equal(MovementSource.DevelopmentFunding, movement.Source);
        Assert.Equal(funding.Id, movement.DevelopmentFundingId);
        Assert.Null(movement.TransferId);
    }

    [Fact]
    public void ForCompletedTransfer_with_mismatched_wallets_throws_argument_exception()
    {
        var sourceWallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var destWallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var otherWallet = Domain.Wallet.Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var transfer = Transfer.Complete(
            sourceWallet.Id,
            destWallet.Id,
            Money.From(100m),
            Guid.NewGuid(),
            new string('1', 64),
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            Movement.ForCompletedTransfer(transfer, otherWallet, destWallet));
    }
}
