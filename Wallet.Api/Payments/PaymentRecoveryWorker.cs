using Wallet.Application;

namespace Wallet.Api.Payments;

public sealed class PaymentRecoveryWorker(IServiceScopeFactory scopes, PaymentSettings settings,
    ILogger<PaymentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IPaymentStore>();
                    var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
                    foreach (var payment in await store.DueAsync(stoppingToken))
                    {
                        try { await processor.ReconcileAsync(payment, stoppingToken); }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception ex)
                        { logger.LogWarning("Recuperación de pago pendiente. PaymentId={PaymentId}, Tipo={Type}", payment.Id, ex.GetType().Name); }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogWarning("Recuperación de pagos no disponible. Tipo={Type}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
