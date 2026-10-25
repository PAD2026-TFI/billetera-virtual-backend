 using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
namespace Wallet.Api.Security;
public sealed class SecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Wallet — identidad y billetera";
        document.Info.Version = "0.2.0-prototype";
        document.Info.Description = "Registro, login, perfil, creación de billetera y consulta de saldo. Identity:Enabled=false devuelve 503 en /api. Ejemplos en Wallet.Api.http.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        foreach (var path in document.Paths)
        {
            if (!path.Key.StartsWith("/api/users/me", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/wallets", StringComparison.Ordinal)
                && !string.Equals(path.Key, "/api/wallets/me", StringComparison.Ordinal)) continue;
            if (path.Value.Operations is null) continue;
            foreach (var operation in path.Value.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [] });
            }
        }
        return Task.CompletedTask;
    }
}
