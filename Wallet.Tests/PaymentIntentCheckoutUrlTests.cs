using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

public sealed class PaymentIntentCheckoutUrlTests
{
    [Fact]
    public async Task Integration_create_and_read_return_the_configured_frontend_checkout_not_the_api_host()
    {
        await using var host = new IdentityTestHost();
        using var app = host.WithWebHostBuilder(builder => builder.UseSetting("FrontendUrl", "https://checkout.example.test"));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://api.example.test") });
        var registration = await client.PostAsJsonAsync("/api/v1/integrations/register",
            new RegisterExternalClientRequest("Comercio de prueba", "checkout-url@example.com", null));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var merchant = (await registration.Content.ReadFromJsonAsync<RegisterExternalClientResponse>())!;
        client.DefaultRequestHeaders.Add("X-Api-Key", merchant.ApiKey);
        var created = await client.PostAsJsonAsync("/api/v1/integrations/payment-links", new { amount = 1500, title = "Checkout configurado" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = (await created.Content.ReadFromJsonAsync<PaymentIntentResponse>())!;
        Assert.Equal($"https://checkout.example.test/checkout?token={link.Token}", link.CheckoutUrl);
        Assert.Equal($"https://api.example.test/api/v1/public/payment-links/{link.Token}/qr", link.QrCodeUrl);
        var response = await client.GetAsync($"/api/v1/integrations/payment-links/{link.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = (await response.Content.ReadFromJsonAsync<PaymentIntentResponse>())!;
        Assert.Equal(link.CheckoutUrl, fetched.CheckoutUrl);
        Assert.Equal(link.QrCodeUrl, fetched.QrCodeUrl);
        Assert.Equal(link.Id, fetched.Id);
    }
}
