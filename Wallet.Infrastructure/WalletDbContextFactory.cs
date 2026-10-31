using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Wallet.Infrastructure;

public sealed class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        // Cadena ficticia para diseño y generación de migraciones
        optionsBuilder.UseSqlServer("Server=localhost;Database=WalletDev;User Id=sa;Password=DesignPassword123!;TrustServerCertificate=True");
        return new WalletDbContext(optionsBuilder.Options);
    }
}
