using Wallet.Domain;

namespace Wallet.Tests;

public static class MoneyTestExtensions
{
    public static decimal Sum(this IEnumerable<Money> source) => source.Select(m => m.Amount).Sum();
}

public class Assert : Xunit.Assert
{
    public static void Equal(decimal expected, Money actual) =>
        Xunit.Assert.Equal(expected, actual.Amount);

    public static void Equal(IEnumerable<decimal> expected, IEnumerable<Money> actual) =>
        Xunit.Assert.Equal(expected, actual.Select(m => m.Amount));

    public static void Contains(decimal expected, IEnumerable<Money> collection) =>
        Xunit.Assert.Contains(expected, collection.Select(m => m.Amount));
}
