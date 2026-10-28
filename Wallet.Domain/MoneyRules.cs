namespace Wallet.Domain;

public static class MoneyRules
{
    public const decimal MaxBalance = 9999999999999999.99m;
    public static void ValidatePositive(decimal amount)
    {
        if (amount <= 0 || amount > MaxBalance || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Importe positivo de hasta dos decimales fuera de rango.", nameof(amount));
    }
}
