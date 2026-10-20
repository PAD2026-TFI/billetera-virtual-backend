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
            StorageUnavailableException => 503,
            UnauthorizedAccessException => 401,
            BadHttpRequestException e => e.StatusCode,
            _ => 500
        };
        // No se registran payloads, tokens, conexión SQL ni mensaje bruto de la excepción.
        logger.LogError("Solicitud fallida. Tipo={Type}, TraceId={TraceId}",
            exception.GetType().Name, context.TraceIdentifier);
        await Results.Problem(statusCode: status, title: status == 503 ? "Servicio no disponible" : "Solicitud no completada",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier,
                ["code"] = status == 503 ? "STORAGE_UNAVAILABLE" : "REQUEST_FAILED" }).ExecuteAsync(context);
        return true;
    }
}
