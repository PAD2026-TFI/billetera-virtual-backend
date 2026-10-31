using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wallet.Application;
namespace Wallet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connection)
    {
        services.AddDbContext<WalletDbContext>(o => o.UseSqlServer(connection,
            sql => sql.CommandTimeout(15)));
        services.AddScoped<IMovementHistoryReader, SqlMovementHistoryReader>();
        services.AddScoped<ITransferStore, SqlTransferStore>();
        services.AddScoped<IWalletRepository, SqlWalletRepository>();
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IExternalClientRepository, SqlExternalClientRepository>();
        services.AddScoped<IPasswordService, IdentityPasswordService>();
        return services;
    }
}
