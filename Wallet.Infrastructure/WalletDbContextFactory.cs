using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Wallet.Infrastructure;

public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("pad2026-wallet-initial")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=WalletDb;Trusted_Connection=True;MultipleActiveResultSets=true";

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new WalletDbContext(optionsBuilder.Options);
    }
}
