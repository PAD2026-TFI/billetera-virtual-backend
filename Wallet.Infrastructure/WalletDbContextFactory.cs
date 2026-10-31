using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wallet.Infrastructure;

public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION")
            ?? "Server=localhost,1433;Database=WalletDb;User Id=sa;Password=Your_Strong_Password123!;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseSqlServer(connectionString);
        return new WalletDbContext(optionsBuilder.Options);
    }
}
