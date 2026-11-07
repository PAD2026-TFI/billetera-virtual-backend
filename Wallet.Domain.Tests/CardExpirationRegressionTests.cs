using Wallet.Domain;

namespace Wallet.Domain.Tests;

public sealed class CardExpirationRegressionTests
{
    private static readonly DateTimeOffset Now = new(2032, 11, 7, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, 2032)]
    [InlineData(12, 2031)]
    [InlineData(12, 10000)]
    public void Creation_rejects_past_months_and_invalid_years(int month, int year) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Card.Create(Guid.NewGuid(), "Titular", "Visa", CardType.Debit, "1234", month, year, Now));

    [Fact]
    public void Current_month_is_valid_until_utc_month_changes()
    {
        var card = Card.Create(Guid.NewGuid(), "Titular", "Visa", CardType.Debit, "1234", 11, 2032, Now);
        Assert.False(card.IsExpired(new DateTimeOffset(2032, 11, 30, 23, 59, 59, TimeSpan.Zero).AddTicks(9999999)));
        Assert.True(card.IsExpired(new DateTimeOffset(2032, 12, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Last_four_reject_unicode_digits() =>
        Assert.Throws<ArgumentException>(() =>
            Card.Create(Guid.NewGuid(), "Titular", "Visa", CardType.Debit, "１２３４", 12, 2032, Now));

    [Fact]
    public void Maximum_year_does_not_overflow_when_checking_expiration()
    {
        var card = Card.Create(Guid.NewGuid(), "Titular", "Visa", CardType.Debit, "1234", 12, 9999, Now);
        Assert.False(card.IsExpired(DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Request_rejects_foreign_and_unowned_addresses()
    {
        var owner = Guid.NewGuid();
        var address = Address.CreateDraft("Calle", "1", "Ciudad", "Provincia", "1000", Now, userId: owner);
        address.MarkValid(0, 0, null, Now);
        Assert.Throws<InvalidOperationException>(() => CardRequest.Create(Guid.NewGuid(), address, Now));
        var legacy = Address.CreateDraft("Calle", "1", "Ciudad", "Provincia", "1000", Now);
        legacy.MarkValid(0, 0, null, Now);
        Assert.Throws<InvalidOperationException>(() => CardRequest.Create(owner, legacy, Now));
    }
}
