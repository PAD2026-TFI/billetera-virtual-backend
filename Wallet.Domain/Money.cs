namespace Wallet.Domain
{
    public readonly record struct Money : IComparable<Money>
    {
        public const string DefaultCurrency = "ARS";
        public const decimal MaxAllowedAmount = 9999999999999999.99m;

        public decimal Amount { get; }
        public string Currency { get; }

        private Money(decimal amount, string currency) 
        {
            Amount = amount;
            Currency = currency;
        }

        public static Money Zero(string currency = DefaultCurrency) => new(0m, ValidateCurrency(currency));

        public static Money From(decimal amount, string currency = DefaultCurrency)
        {
            var validatedCurrency = ValidateCurrency(currency);
            ValidateScaleAndBounds(amount);
            return new Money(amount, validatedCurrency);
        }

        public static Money Positive(decimal amount, string currency = DefaultCurrency)
        {
            if (amount <= 0)
                throw new ArgumentException("Amount must be strictly greater than zero.", nameof(amount));

            return From(amount, currency);
        }

        public static Money operator +(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            var sum = left.Amount + right.Amount;
            if (sum > MaxAllowedAmount)
                throw new OverflowException($"Sum exceeds max allowed ammount: ({MaxAllowedAmount}).");

            return new Money(sum, left.Currency);
        }

        public static Money operator -(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            var difference = left.Amount - right.Amount;
            if (difference < 0)
                throw new InvalidOperationException("El importe resultante no puede ser negativo.");

            return new Money(difference, left.Currency);
        }

        public static bool operator >(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            return left.Amount > right.Amount;
        }

        public static bool operator <(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            return left.Amount < right.Amount;
        }

        public static bool operator >=(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            return left.Amount >= right.Amount;
        }

        public static bool operator <=(Money left, Money right)
        {
            EnsureSameCurrency(left, right);
            return left.Amount <= right.Amount;
        }

        public int CompareTo(Money other)
        {
            EnsureSameCurrency(this, other);
            return Amount.CompareTo(other.Amount);
        }

        public static implicit operator decimal(Money m) => m.Amount;

        public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

        private static string ValidateCurrency(string currency)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currency);

            var trimmed = currency.Trim().ToUpperInvariant();

            if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
                throw new ArgumentException(
                    "The currency code must have exactly 3 alphabetic characters.",
                    nameof(currency)
                );

            if (trimmed != DefaultCurrency)
                throw new ArgumentException(
                    $"Currency not supported in this iteration. Only currency supported is {DefaultCurrency}.",
                    nameof(currency)
                );

            return trimmed;
        }

        private static void ValidateScaleAndBounds(decimal amount)
        {
            if (amount < 0)
                throw new ArgumentException("The amount can't be negative.", nameof(amount));

            if (amount > MaxAllowedAmount)
                throw new ArgumentException($"The amount exceeds the maximum operating limit ({MaxAllowedAmount}).", nameof(amount));

            if (decimal.Round(amount, 2) != amount)
                throw new ArgumentException("The amount does not allow fractions smaller than cents (maximum 2 decimals).", nameof(amount));
        }

        private static void EnsureSameCurrency(Money left, Money right)
        {
            if (left.Currency != right.Currency)
                throw new InvalidOperationException($"Currency incompatibility in a monetary operation: {left.Currency} vs {right.Currency}.");
        }
    }
}
