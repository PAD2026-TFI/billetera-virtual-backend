using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Application;
using Wallet.Domain;
using Wallet.Infrastructure.OpenRenaper;
using Xunit;

namespace Wallet.Tests;

public sealed class IdentityVerificationIntegrationTests
{
    private sealed class StubIdentityVerifier : IIdentityVerifier
    {
        public Func<IdentityQuery, CancellationToken, Task<VerifiedIdentity?>>? Handler { get; set; }

        public Task<VerifiedIdentity?> FindAsync(IdentityQuery query, CancellationToken ct = default)
        {
            if (Handler != null)
                return Handler(query, ct);

            return Task.FromResult<VerifiedIdentity?>(null);
        }
    }

    private static (WebApplicationFactory<Program> factory, HttpClient client, TestUserRepository users, StubIdentityVerifier verifier)
        CreateTestApp(bool verificationEnabled = true)
    {
        var verifier = new StubIdentityVerifier();
        var baseHost = new IdentityTestHost();

        var factory = baseHost.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IdentityVerification:Enabled", verificationEnabled ? "true" : "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityVerifier>();
                services.AddSingleton<IIdentityVerifier>(verifier);
            });
        });

        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        return (factory, client, baseHost.Users, verifier);
    }

    [Fact]
    public async Task Register_succeeds_and_marks_identity_verified_when_renaper_confirms_person()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: true);
        await using (app)
        using (client)
        {
            verifier.Handler = (q, _) => Task.FromResult<VerifiedIdentity?>(
                new VerifiedIdentity("30123456", "Ana María Pérez"));

            var request = new RegisterUserRequest
            {
                FirstName = "Ana",
                LastName = "Pérez",
                Email = "ana.perez@example.com",
                Password = "Password!2026",
                Dni = "30123456"
            };

            var response = await client.PostAsJsonAsync("/api/users", request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("/api/v1/me/profile", response.Headers.Location?.ToString());

            var savedUser = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
            Assert.NotNull(savedUser);
            Assert.NotNull(savedUser.IdentityVerifiedAt);
            Assert.Equal("30123456", savedUser.Dni);
        }
    }

    [Fact]
    public async Task Register_returns_422_identity_not_found_when_renaper_does_not_find_person()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: true);
        await using (app)
        using (client)
        {
            verifier.Handler = (_, _) => Task.FromResult<VerifiedIdentity?>(null);

            var request = new RegisterUserRequest
            {
                FirstName = "Juan",
                LastName = "Pérez",
                Email = "juan.perez@example.com",
                Password = "Password!2026",
                Dni = "99999999"
            };

            var response = await client.PostAsJsonAsync("/api/users", request);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetString();
            Assert.Equal("IDENTITY_NOT_FOUND", code);

            var savedUser = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
            Assert.Null(savedUser);
        }
    }

    [Fact]
    public async Task Register_returns_422_identity_mismatch_when_renaper_names_do_not_match()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: true);
        await using (app)
        using (client)
        {
            verifier.Handler = (q, _) => Task.FromResult<VerifiedIdentity?>(
                new VerifiedIdentity("30123456", "Carlos Alberto González"));

            var request = new RegisterUserRequest
            {
                FirstName = "Ana",
                LastName = "Pérez",
                Email = "ana.perez@example.com",
                Password = "Password!2026",
                Dni = "30123456"
            };

            var response = await client.PostAsJsonAsync("/api/users", request);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetString();
            Assert.Equal("IDENTITY_MISMATCH", code);

            var savedUser = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
            Assert.Null(savedUser);
        }
    }

    [Fact]
    public async Task Register_returns_503_when_identity_provider_fails_or_times_out()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: true);
        await using (app)
        using (client)
        {
            verifier.Handler = (_, _) => throw new IdentityProviderUnavailableException("RENAPER timeout");

            var request = new RegisterUserRequest
            {
                FirstName = "Ana",
                LastName = "Pérez",
                Email = "ana.perez@example.com",
                Password = "Password!2026",
                Dni = "30123456"
            };

            var response = await client.PostAsJsonAsync("/api/users", request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetString();
            Assert.Equal("IDENTITY_PROVIDER_UNAVAILABLE", code);

            var savedUser = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
            Assert.Null(savedUser);
        }
    }

    [Fact]
    public async Task Register_returns_409_when_dni_is_already_registered()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: true);
        await using (app)
        using (client)
        {
            verifier.Handler = (q, _) => Task.FromResult<VerifiedIdentity?>(
                new VerifiedIdentity("30123456", "Ana Pérez"));

            // Pre-seed an existing user with DNI 30123456
            var existingUser = User.Create("Ana", "Pérez", "existing@example.com", DateTimeOffset.UtcNow, dni: "30123456");
            await users.AddAsync(existingUser, CancellationToken.None);

            var duplicateRequest = new RegisterUserRequest
            {
                FirstName = "Ana",
                LastName = "Pérez",
                Email = "another.email@example.com",
                Password = "Password!2026",
                Dni = "30123456"
            };

            var response = await client.PostAsJsonAsync("/api/users", duplicateRequest);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.GetProperty("code").GetString();
            Assert.Equal("DNI_ALREADY_REGISTERED", code);
        }
    }

    [Fact]
    public async Task Register_succeeds_without_identity_call_when_verification_is_disabled()
    {
        var (app, client, users, verifier) = CreateTestApp(verificationEnabled: false);
        await using (app)
        using (client)
        {
            // Configure verifier to throw if called; it shouldn't be called when disabled
            verifier.Handler = (_, _) => throw new InvalidOperationException("Verifier should not be called when disabled!");

            var request = new RegisterUserRequest
            {
                FirstName = "Ana",
                LastName = "Pérez",
                Email = "ana.perez@example.com",
                Password = "Password!2026",
                Dni = "30123456"
            };

            var response = await client.PostAsJsonAsync("/api/users", request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var savedUser = await users.FindByEmailAsync(User.NormalizeEmail(request.Email), CancellationToken.None);
            Assert.NotNull(savedUser);
            Assert.Null(savedUser.IdentityVerifiedAt);
        }
    }

    [Fact]
    [Trait("Category", "ExternalIntegration")]
    public async Task ExternalIntegration_OpenRenaper_connectivity_test()
    {
        var baseUrl = Environment.GetEnvironmentVariable("OPENRENAPER_BASE_URL");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Test no configurado para entorno externo local; se considera aprobado en CI/local.
            return;
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var client = new OpenRenaperClient(httpClient, NullLogger<OpenRenaperClient>.Instance);

        var result = await client.FindAsync(new IdentityQuery(IdentityDocumentType.Dni, "00000001"));
        Assert.True(result is null || !string.IsNullOrWhiteSpace(result.Number));
    }
}
