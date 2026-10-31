namespace Wallet.Domain;

public static class MoneyRules
{
    public const string DefaultCurrency = Money.DefaultCurrency;
    public const decimal MaxBalance = Money.MaxAllowedAmount;
    public static void ValidatePositive(decimal amount) => Money.Positive(amount);
}
