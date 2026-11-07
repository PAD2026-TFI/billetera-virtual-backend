using Wallet.Domain;
using Xunit;
using DomainWallet = Wallet.Domain.Wallet;

namespace Wallet.Tests;

public sealed class B2BAndWalletDomainTests
{
    [Fact]
    public void ExternalClient_Create_validates_cuit_and_stores_properties()
    {
        var now = DateTimeOffset.UtcNow;
        var client = ExternalClient.Create("Empresa S.A.", "30712345678", "contacto@empresa.com", now, "Pasarela B2B");

        Assert.Equal("Empresa S.A.", client.CompanyName);
        Assert.Equal("30712345678", client.Cuit);
        Assert.Equal("contacto@empresa.com", client.ContactEmail);
        Assert.Equal("Pasarela B2B", client.Description);
        Assert.True(client.IsActive);
    }

    [Fact]
    public void ExternalClient_Create_throws_for_invalid_cuit()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() =>
            ExternalClient.Create("Empresa", "123", "contacto@empresa.com", now));
    }

    [Fact]
    public void ExternalClient_ConfigureWebhook_and_DeactivateWebhook_work_properly()
    {
        var now = DateTimeOffset.UtcNow;
        var client = ExternalClient.Create("Empresa S.A.", "30712345678", "contacto@empresa.com", now);

        var validSecret = new string('s', 32);
        client.ConfigureWebhook("https://empresa.com/api/webhook", validSecret);

        Assert.Equal("https://empresa.com/api/webhook", client.WebhookUrl);
        Assert.Equal(validSecret, client.WebhookSecret);

        client.DeactivateWebhook();
        Assert.Null(client.WebhookUrl);
        Assert.Null(client.WebhookSecret);
    }

    [Fact]
    public void ExternalClient_ConfigureWebhook_rejects_short_secret_or_invalid_url()
    {
        var now = DateTimeOffset.UtcNow;
        var client = ExternalClient.Create("Empresa S.A.", "30712345678", "contacto@empresa.com", now);

        Assert.Throws<ArgumentException>(() =>
            client.ConfigureWebhook("not-a-url", new string('s', 32)));

        Assert.Throws<ArgumentException>(() =>
            client.ConfigureWebhook("https://empresa.com/webhook", "short_secret"));
    }

    [Fact]
    public void Wallet_CreateForUser_and_ModifyAlias_work_as_expected()
    {
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var cvu = "0000123456789012345678";
        var alias = "luna.rio.cielo";

        var wallet = DomainWallet.CreateForUser(ownerId, cvu, alias, now);

        Assert.Equal(ownerId, wallet.OwnerId);
        Assert.Equal(cvu, wallet.Cvu);
        Assert.Equal(alias, wallet.Alias);

        wallet.ModifyAlias("sol.mar.arena");
        Assert.Equal("sol.mar.arena", wallet.Alias);
    }

    [Fact]
    public void Wallet_CreateForUser_rejects_invalid_cvu_prefix_or_length()
    {
        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            DomainWallet.CreateForUser(ownerId, "1234567890123456789012", "luna.rio.cielo", now));

        Assert.Throws<ArgumentException>(() =>
            DomainWallet.CreateForUser(ownerId, "000012345", "luna.rio.cielo", now));
    }
}
