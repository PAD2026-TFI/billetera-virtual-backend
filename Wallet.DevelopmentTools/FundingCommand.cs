using System.Globalization;
using Microsoft.Data.SqlClient;
using Wallet.Application;

namespace Wallet.DevelopmentTools;

public static class FundingCommand
{
    public static DevelopmentFundingRequest Validate(string[] args, string? environment, string? connection)
    {
        if (!string.Equals(environment, "Development", StringComparison.Ordinal))
            throw new FundingRejectedException("DEVELOPMENT_ONLY");
        if (args.Length != 12 || args[0] != "fund-wallet" || args[^1] != "--confirm-development-database")
            throw new ArgumentException("Uso: fund-wallet --wallet-id UUID --amount 1000.00 --idempotency-key UUID --server SERVIDOR --database BASE --confirm-development-database");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length - 1; i += 2)
            if (!values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Parámetro duplicado.");
        var expected = new[] { "--wallet-id", "--amount", "--idempotency-key", "--server", "--database" };
        if (values.Count != expected.Length || expected.Any(k => !values.ContainsKey(k)))
            throw new ArgumentException("Parámetros inválidos.");
        if (string.IsNullOrWhiteSpace(connection)) throw new FundingRejectedException("CONNECTION_NOT_CONFIGURED");
        var sql = new SqlConnectionStringBuilder(connection);
        if (string.IsNullOrWhiteSpace(sql.InitialCatalog) || string.IsNullOrWhiteSpace(sql.DataSource)
            || !string.Equals(sql.InitialCatalog, values["--database"], StringComparison.Ordinal)
            || !string.Equals(sql.DataSource, values["--server"], StringComparison.Ordinal)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(sql.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new FundingRejectedException("DATABASE_NOT_CONFIRMED");
        // No acepto separadores de miles ni coma decimal para evitar importes ambiguos.
        if (!decimal.TryParse(values["--amount"], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || !Guid.TryParse(values["--wallet-id"], out var walletId) || walletId == Guid.Empty
            || !Guid.TryParse(values["--idempotency-key"], out var key) || key == Guid.Empty)
            throw new ArgumentException("Importe o identificadores inválidos; uso punto decimal.");
        global::Wallet.Domain.Entities.Status.MoneyRules.ValidatePositive(amount);
        return new(walletId, amount, key);
    }
}
