using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Wallet.Domain;

namespace Wallet.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<WalletOwner> WalletOwners => Set<WalletOwner>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalClient> ExternalClients => Set<ExternalClient>();
    public DbSet<global::Wallet.Domain.Wallet> Wallets => Set<global::Wallet.Domain.Wallet>();
    public DbSet<DevelopmentFunding> DevelopmentFundings => Set<DevelopmentFunding>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CardRequest> CardRequests => Set<CardRequest>();
    public DbSet<PaymentLink> PaymentLinks => Set<PaymentLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
