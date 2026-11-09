using System.Net;
using System.Net.Http.Json;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class PaymentIntentsControllerTests
{
    [Fact]
    public async Task Merchant_registration_creates_client_and_wallet_and_returns_api_key()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();

        var registerReq = new RegisterExternalClientRequest(
            CompanyName: "Tienda Tech B2B",
            ContactEmail: "b2b@tiendatech.com",
            Description: "Tienda de electrónica",
            Cuit: "30712345678");

        var response = await client.PostAsJsonAsync("/api/v1/merchants/register", registerReq);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<RegisterExternalClientResponse>();
        Assert.NotNull(data);
        Assert.NotEqual(Guid.Empty, data.Id);
        Assert.NotEqual(Guid.Empty, data.WalletId);
        Assert.False(string.IsNullOrWhiteSpace(data.ApiKey));
        Assert.Equal("30712345678", data.Cuit);
    }

    [Fact]
    public async Task Payment_intent_requires_x_api_key_header()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();

        var payload = new CreatePaymentIntentRequest(
            Amount: 1500m,
            Title: "Remera oversize");

        var unauthRes = await client.PostAsJsonAsync("/api/v1/payment-intents", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
    }

    [Fact]
    public async Task Payment_intent_with_valid_api_key_creates_intent_with_checkout_and_qr_urls()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();

        // 1. Register merchant
        var registerReq = new RegisterExternalClientRequest(
            CompanyName: "Indumentaria SAS",
            ContactEmail: "contacto@indumentaria.com",
            Description: "Ropa urbana",
            Cuit: "30987654321");

        var regRes = await client.PostAsJsonAsync("/api/v1/merchants/register", registerReq);
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var merchant = await regRes.Content.ReadFromJsonAsync<RegisterExternalClientResponse>();
        Assert.NotNull(merchant);

        // 2. Create Payment Intent using X-Api-Key
        var intentReq = new CreatePaymentIntentRequest(
            Amount: 2500m,
            Title: "Buzo Hoodie Negra",
            Description: "Talle XL",
            Currency: "ARS",
            Type: PaymentLinkType.SingleUse,
            TtlMinutes: 60);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment-intents")
        {
            Content = JsonContent.Create(intentReq)
        };
        request.Headers.Add("X-Api-Key", merchant.ApiKey);

        var intentRes = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, intentRes.StatusCode);

        var intent = await intentRes.Content.ReadFromJsonAsync<PaymentIntentResponse>();
        Assert.NotNull(intent);
        Assert.NotEqual(Guid.Empty, intent.Id);
        Assert.False(string.IsNullOrWhiteSpace(intent.Token));
        Assert.Equal(2500m, intent.Amount);
        Assert.Equal("ARS", intent.Currency);
        Assert.Equal("Buzo Hoodie Negra", intent.Title);
        Assert.Contains($"/checkout?token={intent.Token}", intent.CheckoutUrl);
        Assert.Contains($"/api/v1/public/payment-links/{intent.Token}/qr", intent.QrCodeUrl);

        // 3. Query intent status by ID
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/payment-intents/{intent.Id}");
        getReq.Headers.Add("X-Api-Key", merchant.ApiKey);

        var getRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var fetched = await getRes.Content.ReadFromJsonAsync<PaymentIntentResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(intent.Id, fetched.Id);
        Assert.Equal(intent.Token, fetched.Token);
    }

    [Fact]
    public async Task V1_auth_register_and_login_flow()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();

        var registerReq = new RegisterUserRequest
        {
            Dni = "40123456",
            FirstName = "Carlos",
            LastName = "Tevez",
            Email = "carlos.tevez@example.com",
            Password = IdentityTestHost.Password
        };

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", registerReq);
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);

        var loginReq = new LoginRequest
        {
            Email = "carlos.tevez@example.com",
            Password = IdentityTestHost.Password
        };

        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        var tokenRes = await loginRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokenRes);
        Assert.False(string.IsNullOrWhiteSpace(tokenRes.AccessToken));
    }
}
