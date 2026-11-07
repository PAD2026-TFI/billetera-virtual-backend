using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using Wallet.Api.Routing;

namespace Wallet.Api.Security;

public sealed class SecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Wallet - clientes, comercios y checkout";
        document.Info.Version = "v1";
        document.Info.Description = "Clientes: /api/v1/me con JWT. Apps externas: /api/v1/integrations con X-Api-Key. "
            + "Checkout público: /api/v1/public. Administración: /api/v1/admin con JWT Admin. "
            + "Las rutas anteriores siguen disponibles por compatibilidad y se omiten de este catálogo.";
        document.Components ??= new();
        document.Components.SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
        {
            ["Bearer"] = new() { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "JWT del usuario o administrador." },
            ["ApiKey"] = new() { Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header, Description = "API Key del comercio o aplicación externa." }
        };

        foreach (var legacy in ApiEndpointCatalog.Routes.Keys)
            document.Paths.Remove(ApiEndpointCatalog.OpenApiPath(legacy));

        foreach (var description in context.DescriptionGroups.SelectMany(g => g.Items))
        {
            var route = "/" + description.RelativePath?.Split('?')[0];
            if (!document.Paths.TryGetValue(route, out var path) || !Enum.TryParse<OperationType>(description.HttpMethod, true, out var method)
                || !path.Operations.TryGetValue(method, out var operation)) continue;
            var metadata = description.ActionDescriptor.EndpointMetadata;
            operation.Security = [];
            if (!metadata.OfType<IAllowAnonymous>().Any())
            {
                var external = metadata.OfType<IAuthorizeData>().Any(a => a.Policy == "ExternalClient");
                var scheme = new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = external ? "ApiKey" : "Bearer" }
                };
                operation.Security.Add(new OpenApiSecurityRequirement { [scheme] = [] });
            }
            operation.Tags = [new OpenApiTag { Name = ApiEndpointCatalog.Group(route) }];
            var controller = description.ActionDescriptor.RouteValues["controller"] ?? "";
            var action = description.ActionDescriptor.RouteValues["action"] ?? "";
            operation.Summary = ApiEndpointCatalog.Summary(controller, action);
            operation.OperationId = controller + "_" + action;
            if (controller == "CardRequests" && action == "CreateRequest")
            {
                operation.Description = "Enviá deliveryAddress con calle, número, ciudad, provincia, código postal y país, o addressId de una dirección propia. "
                    + "Sin proveedor geográfico configurado se guarda AwaitingAddressValidation. El pedido no puede pasar a fabricación hasta validar ese domicilio.";
                if (operation.RequestBody?.Content.TryGetValue("application/json", out var content) == true)
                    content.Example = new Microsoft.OpenApi.Any.OpenApiObject
                    {
                        ["type"] = new Microsoft.OpenApi.Any.OpenApiString("Debit"),
                        ["deliveryAddress"] = new Microsoft.OpenApi.Any.OpenApiObject
                        {
                            ["street"] = new Microsoft.OpenApi.Any.OpenApiString("Av. Corrientes"),
                            ["number"] = new Microsoft.OpenApi.Any.OpenApiString("1234"),
                            ["city"] = new Microsoft.OpenApi.Any.OpenApiString("CABA"),
                            ["state"] = new Microsoft.OpenApi.Any.OpenApiString("Buenos Aires"),
                            ["postalCode"] = new Microsoft.OpenApi.Any.OpenApiString("1043"),
                            ["country"] = new Microsoft.OpenApi.Any.OpenApiString("Argentina")
                        }
                    };
            }
            if (controller == "Checkout" && action == "PayWithSavedCard")
                operation.Description = "Usá un cardId de GET /api/v1/me/cards y el ID del link de cobro. Para débito se descuenta saldo de tu billetera y se acredita al comercio, "
                    + "sin PAN ni CVV. Idempotency-Key es obligatorio en débito: reutilizalo al reintentar la misma compra. Saldo insuficiente devuelve 409.";
            if (controller == "Checkout" && action == "Pay")
                operation.Description = "Para tarjeta asociada: paymentMethod=Card y cardId, con JWT e Idempotency-Key. Para billetera se requiere JWT. "
                    + "El objeto card con PAN/CVV corresponde al simulador de tarjetas externas y no identifica una tarjeta emitida por esta billetera.";
        }
        document.Tags = document.Paths.Values.SelectMany(p => p.Operations.Values).SelectMany(o => o.Tags)
            .DistinctBy(t => t.Name).OrderBy(t => t.Name).ToList();
        return Task.CompletedTask;
    }
}
