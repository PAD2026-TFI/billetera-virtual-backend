using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class CardTests
{
    [Fact]
    public void Create_with_valid_parameters_initializes_active_card()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var card = Card.Create(userId, "Fernando Jimenez", "Visa", CardType.Debit, "4532", 12, 2028, now);

        Assert.NotEqual(Guid.Empty, card.Id);
        Assert.Equal(userId, card.UserId);
        Assert.Equal("Fernando Jimenez", card.CardHolderName);
        Assert.Equal("Visa", card.Brand);
        Assert.Equal(CardType.Debit, card.Type);
        Assert.Equal("4532", card.CardNumberLast4);
        Assert.Equal(12, card.ExpirationMonth);
        Assert.Equal(2028, card.ExpirationYear);
        Assert.True(card.IsActive);
        Assert.StartsWith("tok_", card.CardToken);
        Assert.Equal(now.ToUniversalTime(), card.CreatedAt);
    }

    [Fact]
    public void Create_with_empty_user_id_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(() =>
            Card.Create(Guid.Empty, "Fernando Jimenez", "Visa", CardType.Debit, "4532", 12, 2028, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_with_invalid_holder_name_throws_argument_exception(string? holderName)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            Card.Create(Guid.NewGuid(), holderName!, "Visa", CardType.Debit, "4532", 12, 2028, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("abcd")]
    public void Create_with_invalid_last_four_throws_argument_exception(string invalidLast4)
    {
        Assert.Throws<ArgumentException>(() =>
            Card.Create(Guid.NewGuid(), "Fernando Jimenez", "Visa", CardType.Debit, invalidLast4, 12, 2028, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Create_with_invalid_month_throws_argument_out_of_range_exception(int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Card.Create(Guid.NewGuid(), "Fernando Jimenez", "Visa", CardType.Debit, "4532", month, 2028, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_with_past_year_throws_argument_out_of_range_exception()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Card.Create(Guid.NewGuid(), "Fernando Jimenez", "Visa", CardType.Debit, "4532", 12, 2020, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Deactivate_and_Activate_changes_active_flag()
    {
        var card = Card.Create(Guid.NewGuid(), "Fernando Jimenez", "Visa", CardType.Debit, "4532", 12, 2028, DateTimeOffset.UtcNow);
        Assert.True(card.IsActive);

        card.Deactivate();
        Assert.False(card.IsActive);

        card.Activate();
        Assert.True(card.IsActive);
    }

    [Fact]
    public void IsExpired_checks_against_end_of_expiration_month()
    {
        var card = Card.Create(Guid.NewGuid(), "Fernando Jimenez", "Visa", CardType.Debit, "4532", 6, 2027, DateTimeOffset.UtcNow);

        Assert.False(card.IsExpired(new DateTimeOffset(2027, 6, 15, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(card.IsExpired(new DateTimeOffset(2027, 6, 30, 23, 59, 59, TimeSpan.Zero)));
        Assert.True(card.IsExpired(new DateTimeOffset(2027, 7, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
