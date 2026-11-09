using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Wallet.Tests;

public sealed class SwaggerToolsTests
{
    [Theory]
    [InlineData("Development", true, HttpStatusCode.OK)]
    [InlineData("Development", false, HttpStatusCode.NotFound)]
    [InlineData("Production", true, HttpStatusCode.NotFound)]
    [InlineData("Production", false, HttpStatusCode.NotFound)]
    public async Task Documentation_requires_development_and_explicit_opt_in(string environment, bool enabled, HttpStatusCode expected)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment(environment).UseSetting("Identity:Enabled", "false")
             .UseSetting("Documentation:Enabled", enabled.ToString()));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/swagger/index.html", "/swagger/wallet-tools.js", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
        }
        if (expected == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
            Assert.False(document.RootElement.GetProperty("paths").TryGetProperty("/swagger/wallet-tools.js", out _));
        }
    }

    [Fact]
    public async Task Documentation_is_disabled_by_default_even_in_development()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment("Development").UseSetting("Identity:Enabled", "false"));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
