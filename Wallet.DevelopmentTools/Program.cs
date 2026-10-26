using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wallet.Application;
using Wallet.DevelopmentTools;
using Wallet.Infrastructure;

if (args is ["--help"])
{
    Console.WriteLine("DOTNET_ENVIRONMENT=Development; uso User Secrets de Wallet.Api o ConnectionStrings__DefaultConnection.");
    Console.WriteLine("fund-wallet --wallet-id UUID --amount 1000.00 --idempotency-key UUID --server SERVIDOR --database BASE --confirm-development-database");
    return 0;
}

try
{
    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    if (environment != "Development") throw new FundingRejectedException("DEVELOPMENT_ONLY");
    var config = new ConfigurationBuilder().AddUserSecrets("pad2026-wallet-initial")
        .AddEnvironmentVariables().Build();
    var connection = config.GetConnectionString("DefaultConnection");
    var request = FundingCommand.Validate(args, environment, connection);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var options = new DbContextOptionsBuilder<WalletDbContext>().UseSqlServer(connection,
        sql => sql.CommandTimeout(15)).Options;
    var service = new DevelopmentFundingService(new SqlDevelopmentFundingStore(options, TimeProvider.System));
    var result = await service.FundAsync(request, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}
catch (FundingRejectedException e) { Console.Error.WriteLine(e.Code); return 2; }
catch (ArgumentException) { Console.Error.WriteLine("INVALID_INPUT: reviso identificadores, importe, parámetros y conexión."); return 2; }
catch (OperationCanceledException) { Console.Error.WriteLine("RESULT_UNKNOWN: consulto o reintento conservando la misma clave."); return 3; }
catch (StorageUnavailableException) { Console.Error.WriteLine("STORAGE_UNAVAILABLE: verifico la base y reintento con la misma clave; el commit podría haber sido confirmado."); return 3; }
catch (Exception) { Console.Error.WriteLine("REQUEST_FAILED: no informo éxito; verifico el estado usando la misma clave."); return 1; }
