using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

public sealed class CardFormatRegressionTests
{
    private readonly CardSimulatorService _simulator = new(new FixedClock());
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
    }

    [Theory]
    [InlineData("378282246310005")]
    [InlineData("4222222222222")]
    [InlineData("4532 0151 1283 0366")]
    [InlineData("4532015112830366x")]
    [InlineData("４５３２０１５１１２８３０３６６")]
    public void Rejects_numbers_outside_exact_ascii_format(string number)
    {
        var ex = Assert.Throws<CardPaymentRejectedException>(() =>
            _simulator.Authorize(new(number, "Titular", 12, 2030, "123")));
        Assert.Equal("INVALID_CARD_NUMBER", ex.Code);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData(" 123")]
    [InlineData("１２３")]
    [InlineData("")]
    public void Rejects_cvv_outside_exact_ascii_format(string cvv)
    {
        var ex = Assert.Throws<CardPaymentRejectedException>(() =>
            _simulator.Authorize(new("4532015112830366", "Titular", 12, 2030, cvv)));
        Assert.Equal("INVALID_CVV", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Rejects_invalid_expiration_year(int year)
    {
        var ex = Assert.Throws<CardPaymentRejectedException>(() =>
            _simulator.Authorize(new("4532015112830366", "Titular", 12, year, "123")));
        Assert.Equal("INVALID_EXPIRATION_YEAR", ex.Code);
    }
}
