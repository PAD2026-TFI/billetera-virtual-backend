using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Wallet.Api.Security;

public sealed class SecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Wallet — identidad, billetera y pasarela B2B";
        document.Info.Version = "0.2.0-prototype";
        document.Info.Description = "Registro, login, perfil, creación de billetera, consulta de saldo y pasarela B2B (Gateway). Ejemplos en Wallet.Api.http.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token JWT para usuarios de la billetera (Bearer {token})"
        };

        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "X-Api-Key",
            In = ParameterLocation.Header,
            Description = "Clave de API para clientes externos de la pasarela B2B (X-Api-Key: {apiKey})"
        };

        var apiKeyScheme = new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } };
        var bearerScheme = new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } };

        foreach (var path in document.Paths)
        {
            if (path.Value.Operations is null) continue;

            var route = path.Key;

            // Rutas públicas que no requieren ninguna autenticación
            if (string.Equals(route, "/api/auth/token", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/v1/auth/register", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/users", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/admin/register", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/health", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/v1/gateway/clients", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/v1/merchants/register", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/swagger/wallet-tools.js", StringComparison.OrdinalIgnoreCase) ||
                route.StartsWith("/api/v1/checkout/links/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route, "/api/v1/checkout/pay", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Pasarela B2B: autenticación obligatoria mediante X-Api-Key
            if (string.Equals(route, "/api/v1/checkout/links", StringComparison.OrdinalIgnoreCase) ||
                route.StartsWith("/api/v1/gateway", StringComparison.OrdinalIgnoreCase) ||
                route.StartsWith("/api/v1/payment-intents", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var operation in path.Value.Operations.Values)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement { [apiKeyScheme] = [] });
                }
                continue;
            }

            // Todas las demás rutas protegidas usan Bearer (JWT)
            foreach (var operation in path.Value.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement { [bearerScheme] = [] });
            }
        }
        return Task.CompletedTask;
    }
}
