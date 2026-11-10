using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

[Collection("SqlIntegration")]
[Trait("Category", "Sql")]
public sealed class SqlRegistrationEmailTests
{
    private const string SendPath = "/api/v1/auth/email-verification";
    private const string ConfirmPath = SendPath + "/confirm";
    private const string RegisterPath = "/api/v1/auth/register";

    [AzureSqlFact]
    public async Task Registration_requires_a_single_use_proof_for_the_same_email()
    {
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        var sender = new RecordingSender();
        var clock = new TestClock();
        await using var app = WithEmail(host, sender, clock);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var email = $"email-qa-{Guid.NewGuid():N}@example.com";

        Assert.Equal(HttpStatusCode.BadRequest, (await Register(client, email, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        var code = Assert.Single(sender.Messages).Code;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        Assert.Single(sender.Messages);

        var wrong = code == "000000" ? "000001" : "000000";
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, wrong)).StatusCode);
        using var confirmed = await Confirm(client, email.ToUpperInvariant(), code);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var proof = (await confirmed.Content.ReadFromJsonAsync<RegistrationEmailProof>())!.VerificationToken;
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, code)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Register(client, $"other-{email}", proof)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Register(client, email, proof)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Register(client, email, proof)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = IdentityTestHost.Password })).StatusCode);
    }

    [AzureSqlFact]
    public async Task Expired_codes_and_exhausted_attempts_require_a_new_code()
    {
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        var sender = new RecordingSender();
        var clock = new TestClock();
        await using var app = WithEmail(host, sender, clock);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var email = $"email-expiry-{Guid.NewGuid():N}@example.com";

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        var initial = Assert.Single(sender.Messages).Code;
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, initial)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        var code = sender.Messages.Last().Code;
        var wrong = code == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, code)).StatusCode);
    }

    private static WebApplicationFactory<Program> WithEmail(IdentityTestHost host, RecordingSender sender, TestClock clock) =>
        host.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mail:Enabled", "true");
            builder.UseSetting("Mail:Host", "smtp.example.com");
            builder.UseSetting("Mail:Username", "padi@example.com");
            builder.UseSetting("Mail:Password", "test-only");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRegistrationEmailSender>();
                services.AddSingleton<IRegistrationEmailSender>(sender);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        });

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync(ConfirmPath, new { email, code });

    private static Task<HttpResponseMessage> Register(HttpClient client, string email, string? token) =>
        client.PostAsJsonAsync(RegisterPath, new
        {
            firstName = "Ana", lastName = "Prueba", email, password = IdentityTestHost.Password,
            emailVerificationToken = token
        });

    private sealed class RecordingSender : IRegistrationEmailSender
    {
        public List<(string Email, string Code)> Messages { get; } = [];
        public Task SendCodeAsync(string email, string code, CancellationToken ct)
        {
            Messages.Add((email, code));
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
