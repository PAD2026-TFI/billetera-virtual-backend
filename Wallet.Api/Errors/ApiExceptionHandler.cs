using Microsoft.AspNetCore.Diagnostics;
using Wallet.Application;
namespace Wallet.Api.Errors;
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return false;
        int status = exception switch
        {
            PaymentRejectedException e => e.Code switch
            {
                "PAYMENTS_DISABLED" => 503, "PAYMENT_NOT_FOUND" or "WALLET_NOT_FOUND" => 404,
                "INVALID_SESSION" => 401, "INVALID_INPUT" or "INVALID_AMOUNT" => 400,
                _ => 409
            },
            GatewayException => 503,
            StorageUnavailableException => 503,
            UnauthorizedAccessException => 401,
            BadHttpRequestException e => e.StatusCode,
            _ => 500
        };
        // No se registran payloads, tokens, conexión SQL ni mensaje bruto de la excepción.
        logger.LogError("Solicitud fallida. Tipo={Type}, TraceId={TraceId}",
            exception.GetType().Name, context.TraceIdentifier);
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        var code = exception switch
        {
            PaymentRejectedException payment => payment.Code,
            GatewayException gateway => gateway.Code,
            StorageUnavailableException => "STORAGE_UNAVAILABLE",
            _ => "REQUEST_FAILED"
        };
        await Results.Problem(statusCode: status, title: status == 503 ? "Servicio no disponible" : "Solicitud no completada",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier,
                ["code"] = code }).ExecuteAsync(context);
        return true;
    }
}
