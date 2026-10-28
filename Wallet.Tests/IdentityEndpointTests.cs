using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Application;
using Wallet.Domain.Entities;
using Xunit;
namespace Wallet.Tests;

// Dobles exclusivos del proyecto de pruebas; nunca se registran en la aplicación real.
public sealed class IdentityEndpointTests
{
    private sealed class TestUsers : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _users = new();
        public bool Unavailable { get; set; }
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(_users.GetValueOrDefault(email));
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Unavailable ? throw new StorageUnavailableException(new InvalidOperationException())
                : Task.FromResult(_users.Values.SingleOrDefault(x => x.Id == id));
        public Task AddAsync(User user, CancellationToken ct)
        {
            if (!_users.TryAdd(user.NormalizedEmail, user)) throw new DuplicateEmailException();
            return Task.CompletedTask;
        }
        public Task SaveAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);
    }
    [Fact]
    public async Task Registration_login_profile_and_revocation_follow_contract()
    {
        var users = new TestUsers();
        // Sólo en tests: clave efímera y una conexión que jamás se utiliza.
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Identity:Enabled", "true");
            b.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused;Integrated Security=true");
            b.UseSetting("Jwt:Key", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            b.ConfigureTestServices(s => { s.RemoveAll<IUserRepository>(); s.AddSingleton<IUserRepository>(users); });
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var registration = new RegisterUserRequest { FirstName = "Ana", LastName = "Pérez",
            Email = "ana@example.com", Password = "Frase de prueba 2026!" };
        var registered = await client.PostAsJsonAsync("/api/users", registration);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal("/api/users/me", registered.Headers.Location!.ToString());
        Assert.DoesNotContain("password", await registered.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/users", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = registration.Email, Password = "incorrecta" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            { Email = registration.Email, Password = registration.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        var updated = await client.PutAsJsonAsync("/api/users/me/profile",
            new UpdateProfileRequest { FirstName = "Ana María", LastName = "Pérez" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Ana María", (await updated.Content.ReadFromJsonAsync<UserResponse>())!.FirstName);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/users/me/profile",
            new { firstName = "Ana", lastName = "Pérez", role = "Admin" })).StatusCode);
        users.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/users/me")).StatusCode);
        users.Unavailable = false;
        var user = await users.FindByEmailAsync(User.NormalizeEmail(registration.Email), CancellationToken.None);
        user!.RevokeSessions();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }
}
