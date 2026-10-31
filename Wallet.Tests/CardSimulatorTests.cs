using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

public sealed class CardSimulatorTests
{
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly CardSimulatorService _simulator;

    public CardSimulatorTests()
    {
        _simulator = new CardSimulatorService(_clock);
    }

    [Theory]
    [InlineData("4532015112830366", "Juan Perez", 12, 2030, "123", "Visa")] // Valid Visa test
    [InlineData("5555555555554444", "Maria Gomez", 8, 2029, "456", "Mastercard")] // Valid Mastercard test
    [InlineData("378282246310005", "Carlos Lopez", 11, 2031, "1234", "American Express")] // Valid Amex test
    public void Authorize_with_valid_card_succeeds_and_detects_brand(
        string number, string holder, int month, int year, string cvv, string expectedBrand)
    {
        var details = new CardPaymentDetails(number, holder, month, year, cvv);
        var result = _simulator.Authorize(details);

        Assert.True(result.Approved);
        Assert.Equal(expectedBrand, result.Brand);
        Assert.Equal(number[^4..], result.LastFour);
        Assert.False(string.IsNullOrWhiteSpace(result.AuthorizationCode));
    }

    [Fact]
    public void Authorize_with_invalid_luhn_throws_invalid_card_number()
    {
        var details = new CardPaymentDetails("4532015112830367", "Juan Perez", 12, 2030, "123");
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("INVALID_CARD_NUMBER", ex.Code);
    }

    [Fact]
    public void Authorize_with_empty_holder_throws_invalid_cardholder()
    {
        var details = new CardPaymentDetails("4532015112830366", "  ", 12, 2030, "123");
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("INVALID_CARDHOLDER", ex.Code);
    }

    [Theory]
    [InlineData(13, 2030)]
    [InlineData(0, 2030)]
    public void Authorize_with_invalid_month_throws_invalid_expiration_month(int month, int year)
    {
        var details = new CardPaymentDetails("4532015112830366", "Juan Perez", month, year, "123");
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("INVALID_EXPIRATION_MONTH", ex.Code);
    }

    [Fact]
    public void Authorize_with_expired_card_throws_card_expired()
    {
        var details = new CardPaymentDetails("4532015112830366", "Juan Perez", 1, 2020, "123");
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("CARD_EXPIRED", ex.Code);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    public void Authorize_with_invalid_cvv_throws_invalid_cvv(string cvv)
    {
        var details = new CardPaymentDetails("4532015112830366", "Juan Perez", 12, 2030, cvv);
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("INVALID_CVV", ex.Code);
    }

    [Fact]
    public void Authorize_with_cvv_999_simulates_insufficient_funds()
    {
        // 4532015112830366 is valid Luhn
        var details = new CardPaymentDetails("4532015112830366", "Juan Perez", 12, 2030, "999");
        var ex = Assert.Throws<CardPaymentRejectedException>(() => _simulator.Authorize(details));
        Assert.Equal("INSUFFICIENT_FUNDS", ex.Code);
    }
}
