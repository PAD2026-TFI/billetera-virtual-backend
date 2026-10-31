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

        foreach (var path in document.Paths)
        {
            if (path.Value.Operations is null) continue;

            if (path.Key.StartsWith("/api/v1/gateway", StringComparison.Ordinal))
            {
                if (string.Equals(path.Key, "/api/v1/gateway/clients", StringComparison.Ordinal))
                    continue;

                foreach (var operation in path.Value.Operations.Values)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }] = []
                    });
                }
                continue;
            }

            if (!path.Key.StartsWith("/api/users/me", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/wallets", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/wallets/me", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/wallets/me/movements", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/transfers", StringComparison.Ordinal)) continue;

            foreach (var operation in path.Value.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
                });
            }
        }
        return Task.CompletedTask;
    }
}
