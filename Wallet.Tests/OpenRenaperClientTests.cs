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
            BaseAddress = new Uri("https://openrenaper.test")
        };

        var client = new OpenRenaperClient(httpClient, NullLogger<OpenRenaperClient>.Instance);
        return (client, sentRequests);
    }

    [Fact]
    public async Task FindAsync_returns_verified_identity_when_person_is_found()
    {
        var jsonResponse = """
        {
            "tipo": "dni",
            "numero": "30123456",
            "nombre": "  Ana María  ",
            "apellido": "  Pérez  ",
            "valido": true,
            "fallecido": false
        }
        """;

        var (client, sentRequests) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456"));

        Assert.NotNull(result);
        Assert.Equal("30123456", result.Number);
        Assert.Equal("Ana María", result.FirstName);
        Assert.Equal("Pérez", result.LastName);

        var request = Assert.Single(sentRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/personas/consultar", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FindAsync_returns_null_when_status_is_404_not_found()
    {
        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "99999999"));

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_returns_null_when_valido_flag_is_false()
    {
        var jsonResponse = """
        {
            "tipo": "dni",
            "numero": "30123456",
            "nombre": "Desconocido",
            "apellido": "Desconocido",
            "valido": false
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
    public async Task FindAsync_returns_null_when_person_is_deceased()
    {
        var jsonResponse = """
        {
            "tipo": "dni",
            "numero": "10123456",
            "nombre": "Juan",
            "apellido": "Pérez",
            "valido": true,
            "fallecido": true
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "10123456"));

        Assert.Null(result);
    }

    [Theory]
    [InlineData("", "Perez")]
    [InlineData("Juan", "")]
    [InlineData("   ", "   ")]
    public async Task FindAsync_returns_null_when_names_are_missing_in_response(string firstName, string lastName)
    {
        var jsonResponse = $$"""
        {
            "tipo": "dni",
            "numero": "30123456",
            "nombre": "{{firstName}}",
            "apellido": "{{lastName}}",
            "valido": true
        }
        """;

        var (client, _) = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        }));

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "30123456"));

        Assert.Null(result);
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
