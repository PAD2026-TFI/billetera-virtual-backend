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
        services.AddScoped<ITransferCounterpartyResolver, SqlTransferCounterpartyResolver>();
        services.AddScoped<ITransferStore, SqlTransferStore>();
        services.AddScoped<IDebitCardPaymentStore, SqlDebitCardPaymentStore>();
        services.AddScoped<IDevelopmentFundingStore, SqlDevelopmentFundingStore>();
        services.AddScoped<IWalletRepository, SqlWalletRepository>();
        services.AddScoped<IUserRepository, SqlUserRepository>();
        services.AddScoped<IEmailVerificationRepository, SqlEmailVerificationRepository>();
        services.AddScoped<IRegistrationEmailSender, SmtpRegistrationEmailSender>();
        services.AddScoped<IExternalClientRepository, SqlExternalClientRepository>();
        services.AddScoped<IPaymentLinkRepository, SqlPaymentLinkRepository>();
        services.AddScoped<IPaymentRepository, SqlPaymentRepository>();
        services.AddScoped<IPasswordService, IdentityPasswordService>();
        services.AddScoped<ICardRepository, SqlCardRepository>();
        services.AddScoped<IVirtualCardRepository, SqlCardRepository>();
        services.AddScoped<ICardRequestRepository, SqlCardRequestRepository>();
        services.AddScoped<IAddressRepository, SqlAddressRepository>();
        services.AddScoped<IUnitOfWork, SqlUnitOfWork>();
        services.AddSingleton<ICvuGenerator, CvuGenerator>();
        services.AddSingleton<IAliasGenerator, AliasGenerator>();
        services.AddScoped<UserRegistrationService>();
        return services;
    }
}
