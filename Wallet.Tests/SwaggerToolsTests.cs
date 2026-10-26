using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Wallet.Tests;

public sealed class SwaggerToolsTests
{
    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task Swagger_helper_is_available_only_in_development(string environment, HttpStatusCode expected)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseEnvironment(environment).UseSetting("Identity:Enabled", "false"));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/swagger/wallet-tools.js");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("application/javascript", response.Content.Headers.ContentType?.MediaType);
            using var index = await client.GetAsync("/swagger/index.html");
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Contains("/swagger/wallet-tools.js", await index.Content.ReadAsStringAsync());
        }
    }
}
