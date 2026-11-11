using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
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
public sealed class SqlPasswordResetTests
{
    private const string SendPath = "/api/v1/auth/password-reset";
    private const string ConfirmPath = SendPath + "/confirm";
    private const string NewPassword = "NuevaClave2026";

    [AzureSqlFact]
    public async Task Reset_changes_the_password_and_closes_open_sessions()
    {
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        var sender = new RecordingSender();
        var clock = new TestClock();
        await using var app = WithEmail(host, sender, clock);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var email = $"reset-qa-{Guid.NewGuid():N}@example.com";

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        Assert.Empty(sender.ResetCodes);

        await Register(client, sender, email);
        var session = await Login(client, email, IdentityTestHost.Password);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var token = (await session.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email = email.ToUpperInvariant() })).StatusCode);
        var code = Assert.Single(sender.ResetCodes).Code;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        Assert.Single(sender.ResetCodes);

        var wrong = code == "000000" ? "000001" : "000000";
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, wrong, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, code, "corta")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(client, email, code, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, code, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, email, IdentityTestHost.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, email, NewPassword)).StatusCode);
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        me.Headers.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(me)).StatusCode);
    }

    [AzureSqlFact]
    public async Task Expired_codes_and_exhausted_attempts_keep_the_old_password()
    {
        await using var host = new IdentityTestHost(Environment.GetEnvironmentVariable("WALLET_TEST_CONNECTION"));
        var sender = new RecordingSender();
        var clock = new TestClock();
        await using var app = WithEmail(host, sender, clock);
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var email = $"reset-expiry-{Guid.NewGuid():N}@example.com";
        await Register(client, sender, email);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        var initial = Assert.Single(sender.ResetCodes).Code;
        clock.Now += TimeSpan.FromMinutes(11);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, initial, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(SendPath, new { email })).StatusCode);
        var code = sender.ResetCodes.Last().Code;
        var wrong = code == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, wrong, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, email, code, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, email, IdentityTestHost.Password)).StatusCode);
    }

    private static async Task Register(HttpClient client, RecordingSender sender, string email)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/email-verification", new { email })).StatusCode);
        var code = sender.RegistrationCodes.Last(message => message.Email == email).Code;
        using var confirmed = await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { email, code });
        var proof = (await confirmed.Content.ReadFromJsonAsync<RegistrationEmailProof>())!.VerificationToken;
        var dni = Random.Shared.Next(20_000_000, 49_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            dni, firstName = "Ana", lastName = "Prueba", email, password = IdentityTestHost.Password,
            emailVerificationToken = proof
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
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
                services.RemoveAll<IPasswordResetEmailSender>();
                services.AddSingleton<IRegistrationEmailSender>(sender);
                services.AddSingleton<IPasswordResetEmailSender>(sender);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IStartupFilter, DistinctClientAddresses>();
            });
        });

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string email, string code, string newPassword) =>
        client.PostAsJsonAsync(ConfirmPath, new { email, code, newPassword });

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private sealed class RecordingSender : IRegistrationEmailSender, IPasswordResetEmailSender
    {
        public List<(string Email, string Code)> RegistrationCodes { get; } = [];
        public List<(string Email, string Code)> ResetCodes { get; } = [];

        public Task SendCodeAsync(string email, string code, CancellationToken ct)
        {
            RegistrationCodes.Add((email, code));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(string email, string code, CancellationToken ct)
        {
            ResetCodes.Add((email, code));
            return Task.CompletedTask;
        }
    }

    private sealed class DistinctClientAddresses : IStartupFilter
    {
        private int _next;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, call) =>
            {
                context.Connection.RemoteIpAddress = new IPAddress(Interlocked.Increment(ref _next));
                return call(context);
            });
            next(app);
        };
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
