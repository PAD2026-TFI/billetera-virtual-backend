using System.Globalization;
using Wallet.Domain;
using Xunit;

namespace Wallet.Doamin.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Zero_creates_instance_with_zero_amount_and_default_ARS_currency()
    {
        var money = Money.Zero();

        Assert.Equal(0m, money.Amount);
        Assert.Equal("ARS", money.Currency);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(100.50)]
    [InlineData(780400.00)]
    [InlineData(9999999999999999.99)] //TODO: falla porque llega al test con 10000000000000000 (lo cual falla) y no con el valor indicado 
    public void From_with_valid_amount_and_currency_creates_money_instance(decimal amount)
    {
        var money = Money.From(amount, "ARS");

        Assert.Equal(amount, money.Amount);
        Assert.Equal("ARS", money.Currency);
    }

    [Theory]
    [InlineData("ars")]
    [InlineData(" ArS ")]
    public void From_normalizes_currency_to_uppercase_and_trims_whitespace(string currency)
    {
        var money = Money.From(50m, currency);

        Assert.Equal("ARS", money.Currency);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void From_with_negative_amount_throws_argument_exception(decimal negativeAmount)
    {
        var ex = Assert.Throws<ArgumentException>(() => Money.From(negativeAmount));

        Assert.Contains("The amount can't be negative.", ex.Message);
    }

    [Fact]
    public void From_when_amount_exceeds_maximum_allowed_throws_argument_exception()
    {
        var excessiveAmount = Money.MaxAllowedAmount + 0.01m;

        var ex = Assert.Throws<ArgumentException>(() => Money.From(excessiveAmount));

        Assert.Contains("Monetary amount exceeds operational upper limit", ex.Message); //TODO: Falla porque los mensajes son distintos. Sin embargo, la validación es correcta.
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1.234)]
    [InlineData(10.999)]
    public void From_with_more_than_two_decimal_places_throws_argument_exception(decimal amountWithExcessiveDecimals)
    {
        var ex = Assert.Throws<ArgumentException>(() => Money.From(amountWithExcessiveDecimals));

        Assert.Contains("The amount does not allow fractions smaller than cents (maximum 2 decimals).", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AR")]
    [InlineData("ARSS")]
    [InlineData("123")]
    [InlineData("USD")]
    public void From_with_invalid_or_unsupported_currency_throws_argument_exception(string? currency)
    {
        Assert.Throws<ArgumentException>(() => Money.From(10m, currency!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Positive_with_zero_or_negative_amount_throws_argument_exception(decimal amount)
    {
        var ex = Assert.Throws<ArgumentException>(() => Money.Positive(amount));

        Assert.Contains("Amount must be strictly greater than zero", ex.Message);
    }

    [Fact]
    public void Positive_with_valid_positive_amount_creates_money_instance()
    {
        var money = Money.Positive(10.50m);

        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("ARS", money.Currency);
    }

    [Fact]
    public void Addition_adds_amounts_correctly()
    {
        var m1 = Money.From(150.25m);
        var m2 = Money.From(50.75m);

        var result = m1 + m2;

        Assert.Equal(201.00m, result.Amount);
        Assert.Equal("ARS", result.Currency);
    }

    [Fact]
    public void Addition_exceeding_maximum_allowed_amount_throws_overflow_exception()
    {
        var m1 = Money.From(Money.MaxAllowedAmount);
        var m2 = Money.From(0.01m);

        Assert.Throws<OverflowException>(() => _ = m1 + m2);
    }

    [Fact]
    public void Subtraction_subtracts_amounts_correctly()
    {
        var m1 = Money.From(150.50m);
        var m2 = Money.From(50.25m);

        var result = m1 - m2;

        Assert.Equal(100.25m, result.Amount);
        Assert.Equal("ARS", result.Currency);
    }

    [Fact]
    public void Subtraction_resulting_in_negative_amount_throws_invalid_operation_exception()
    {
        var m1 = Money.From(10m);
        var m2 = Money.From(10.01m);

        Assert.Throws<InvalidOperationException>(() => _ = m1 - m2);
    }

    [Fact]
    public void Comparison_operators_and_CompareTo_evaluate_amounts_correctly()
    {
        var smaller = Money.From(10m);
        var bigger = Money.From(20m);
        var duplicateSmaller = Money.From(10m);

        Assert.True(bigger > smaller);
        Assert.True(smaller < bigger);
        Assert.True(smaller <= duplicateSmaller);
        Assert.True(smaller >= duplicateSmaller);
        Assert.Equal(-1, smaller.CompareTo(bigger));
        Assert.Equal(1, bigger.CompareTo(smaller));
        Assert.Equal(0, smaller.CompareTo(duplicateSmaller));
    }

    [Fact]
    public void Value_object_equality_compares_values_structurally()
    {
        var m1 = Money.From(100m, "ARS");
        var m2 = Money.From(100.00m, "ARS");
        var m3 = Money.From(100.01m, "ARS");

        Assert.Equal(m1, m2);
        Assert.True(m1 == m2);
        Assert.False(m1 != m2);
        Assert.NotEqual(m1, m3);
        Assert.Equal(m1.GetHashCode(), m2.GetHashCode());
    }

    [Fact]
    public void ToString_formats_amount_with_two_decimals_and_currency()
    {
        var money = Money.From(1234.5m); //Esto devuelve $1234,50 ARS -> Fijarse en la coma

        var output = money.ToString(); 

        Assert.Equal("1234.50 ARS", output); 
    }
}