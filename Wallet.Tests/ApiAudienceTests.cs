using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

public sealed class ApiAudienceTests
{
    [Fact]
    public async Task Catalog_separates_audiences_and_declares_actual_authentication()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/api/v1/cards/requests", out _));
        Assert.False(paths.TryGetProperty("/api/v1/gateway/payments", out _));
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var tag = operation.Value.GetProperty("tags")[0].GetString();
                Assert.False(string.IsNullOrWhiteSpace(operation.Value.GetProperty("summary").GetString()));
                var requiresAuthentication = operation.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;
                if (path.Name.StartsWith("/api/v1/me/"))
                {
                    Assert.StartsWith("02 - Clientes", tag);
                    Assert.True(requiresAuthentication);
                    Assert.True(security[0].TryGetProperty("Bearer", out _));
                }
                if (path.Name.StartsWith("/api/v1/integrations/") && !path.Name.EndsWith("/register"))
                {
                    Assert.StartsWith("03 - Apps externas", tag);
                    Assert.True(requiresAuthentication);
                    Assert.True(security[0].TryGetProperty("ApiKey", out _));
                }
                if (path.Name.StartsWith("/api/v1/public/") || path.Name.StartsWith("/health/"))
                    Assert.False(requiresAuthentication);
            }
        }
    }

    [Fact]
    public async Task Canonical_and_legacy_profile_routes_serve_the_same_user()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        var canonical = await client.GetAsync("/api/v1/me/profile");
        var legacy = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, canonical.StatusCode);
        Assert.Equal(await canonical.Content.ReadAsStringAsync(), await legacy.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/integrations/profile")).StatusCode);
    }

    [Fact]
    public async Task Api_key_authenticates_integrations_but_does_not_authorize_client_cards()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        var response = await client.PostAsJsonAsync("/api/v1/integrations/register",
            new RegisterExternalClientRequest("Comercio", "api-audience@example.com", "Prueba", "30712345678"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var merchant = (await response.Content.ReadFromJsonAsync<RegisterExternalClientResponse>())!;
        client.DefaultRequestHeaders.Add("X-Api-Key", merchant.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/integrations/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/cards")).StatusCode);
    }
}
