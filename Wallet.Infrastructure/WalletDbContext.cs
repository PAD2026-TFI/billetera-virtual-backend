using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Wallet.Domain.Entities;
using Wallet.Domain.Entities.Status;
namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
