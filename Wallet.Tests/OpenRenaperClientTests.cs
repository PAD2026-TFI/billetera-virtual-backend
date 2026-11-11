using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application;
using Wallet.Infrastructure.OpenRenaper;
using Xunit;

namespace Wallet.Tests;

public sealed class OpenRenaperClientTests
{
    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private static (OpenRenaperClient client, List<HttpRequestMessage> sentRequests) CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var sentRequests = new List<HttpRequestMessage>();
        var messageHandler = new DelegatingHandlerStub(req =>
        {
            sentRequests.Add(req);
            return handler(req);
        });

        var httpClient = new HttpClient(messageHandler)
        {
            BaseAddress = new Uri("https://api-openrenaper.matiasagustinibanez.com")
        };

        var client = new OpenRenaperClient(httpClient, NullLogger<OpenRenaperClient>.Instance);
        return (client, sentRequests);
    }

    [Fact]
    public async Task FindAsync_returns_verified_identity_when_person_is_found()
    {
        var jsonResponse = """
        {
            "nombre": "  Ana María Pérez  ",
            "dni": "30123456",
            "cuit": "27301234560",
            "edad": 35,
            "sexo": "Femenino",
            "provincia_ubicacion": "Buenos Aires"
        }
        """;

        var (client, sentRequests) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456"));

        Assert.NotNull(result);
        Assert.Equal("30123456", result.Number);
        Assert.Equal("Ana María Pérez", result.FullName);
        Assert.Equal("27301234560", result.Cuit);

        var request = Assert.Single(sentRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/consultas", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FindAsync_returns_null_when_status_is_404_sin_resultados()
    {
        var errorResponse = """
        {
            "codigo": "sin_resultados",
            "mensaje": "No se encontraron coincidencias.",
            "fuente": null,
            "detalles": []
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(errorResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "99999999"));

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_returns_null_when_status_is_409_consulta_ambigua()
    {
        var errorResponse = """
        {
            "codigo": "consulta_ambigua",
            "mensaje": "La consulta tiene varias coincidencias posibles; consultar por CUIT.",
            "fuente": null,
            "detalles": []
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(errorResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456"));

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task FindAsync_returns_null_when_nombre_is_missing_or_empty_in_response(string? nombre)
    {
        var jsonResponse = $$"""
        {
            "nombre": {{(nombre == null ? "null" : $"\"{nombre}\"")}},
            "dni": "30123456",
            "cuit": null,
            "edad": null,
            "sexo": null,
            "provincia_ubicacion": null
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456"));

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_throws_unavailable_exception_on_429_rate_limit()
    {
        var errorResponse = """
        {
            "codigo": "limite_solicitudes_excedido",
            "mensaje": "Se alcanzó el límite de peticiones. Reintentá más tarde.",
            "fuente": null,
            "detalles": [{ "limite": "ip" }]
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(errorResponse, Encoding.UTF8, "application/json")
        }));

        var ex = await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() =>
            client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456")));

        Assert.Contains("límite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task FindAsync_throws_unavailable_exception_on_server_errors(HttpStatusCode statusCode)
    {
        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(statusCode)));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() =>
            client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456")));
    }

    [Fact]
    public async Task FindAsync_throws_unavailable_exception_on_network_failure()
    {
        var (client, _) = CreateClient(_ => throw new HttpRequestException("Connection refused"));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() =>
            client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456")));
    }

    [Fact]
    public async Task FindAsync_throws_unavailable_exception_on_timeout()
    {
        var (client, _) = CreateClient(_ => throw new TaskCanceledException("Request timed out"));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() =>
            client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456")));
    }

    [Fact]
    public async Task FindAsync_throws_unavailable_exception_on_malformed_json_response()
    {
        var malformedJson = "{ not a valid json response ...";

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(malformedJson, Encoding.UTF8, "application/json")
        }));

        await Assert.ThrowsAsync<IdentityProviderUnavailableException>(() =>
            client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456")));
    }
}
