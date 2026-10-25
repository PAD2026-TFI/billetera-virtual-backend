using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wallet.Application;
using Xunit;

namespace Wallet.Tests;

public sealed class AuthenticationContractTests
{
    [Fact]
    public async Task Login_issues_bearer_with_configured_lifetime_and_identity_claims()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = user.Email, Password = IdentityTestHost.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(900, token.ExpiresIn);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("Client", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Id));
        Assert.Equal("0", jwt.Claims.Single(c => c.Type == "session_version").Value);
        Assert.True(response.Headers.CacheControl?.NoStore);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Invalid_credentials_unknown_and_disabled_users_have_same_generic_rejection()
    {
        await using var host = new IdentityTestHost();
        var active = host.Seed();
        var disabled = host.Seed("disabled@example.com");
        disabled.Disable();
        using var client = host.Client();
        var problems = new List<(string?, string?)>();
        foreach (var (email, password) in new[]
        {
            (active.Email, "incorrecta"), ("unknown@example.com", IdentityTestHost.Password),
            (disabled.Email, IdentityTestHost.Password)
        })
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problems.Add((body.RootElement.GetProperty("code").GetString(),
                body.RootElement.GetProperty("title").GetString()));
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        Assert.Single(problems.Distinct());
        Assert.Equal("INVALID_CREDENTIALS", problems[0].Item1);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("algorithm")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("missing:sub")]
    [InlineData("invalid:sub")]
    [InlineData("missing:role")]
    [InlineData("invalid:role")]
    [InlineData("missing:session_version")]
    [InlineData("invalid:session_version")]
    [InlineData("missing:jti")]
    [InlineData("invalid:jti")]
    public async Task Invalid_tokens_cannot_read_or_update_profile(string variant)
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        if (variant != "none") client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user, variant));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/users/me/profile",
            new UpdateProfileRequest { FirstName = "Changed", LastName = "Name" })).StatusCode);
        Assert.Equal("Ana", user.FirstName);
        Assert.Equal(0, host.Users.Saves);
    }

    [Fact]
    public async Task Valid_session_with_non_client_role_is_forbidden()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed(role: "TestObserver");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/users/me/profile",
            new { firstName = "Changed", lastName = "Name" })).StatusCode);
        Assert.Equal(0, host.Users.Saves);
    }

    [Fact]
    public async Task Profile_is_private_and_editing_names_preserves_session()
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        var other = host.Seed("other@example.com");
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        using var response = await client.PutAsJsonAsync("/api/users/me/profile",
            new UpdateProfileRequest { FirstName = "Ana María", LastName = "Pérez" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, owner.SessionVersion);
        Assert.Equal(1, host.Users.Saves);
        Assert.Equal("Ana", other.FirstName);
        using var profile = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var body = JsonDocument.Parse(await profile.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "email", "firstName", "id", "lastName" },
            body.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(owner.Id, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Ana María", body.RootElement.GetProperty("firstName").GetString());
    }

    [Theory]
    [InlineData("role", "Admin")]
    [InlineData("userId", "00000000-0000-0000-0000-000000000001")]
    public async Task Registration_and_profile_reject_unmapped_fields(string field, string value)
    {
        await using var host = new IdentityTestHost();
        var owner = host.Seed();
        var other = host.Seed("other@example.com");
        using var client = host.Client();
        var payload = new Dictionary<string, string>
        {
            ["firstName"] = "Changed", ["lastName"] = "Name", [field] = value
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(owner));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/users/me/profile", payload)).StatusCode);
        payload["email"] = "new@example.com";
        payload["password"] = IdentityTestHost.Password;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/users", payload)).StatusCode);
        Assert.Null(await host.Users.FindByEmailAsync("NEW@EXAMPLE.COM", CancellationToken.None));
        Assert.Equal("Ana", owner.FirstName);
        Assert.Equal("Ana", other.FirstName);
        Assert.Equal(0, host.Users.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_or_disabling_invalidates_previously_valid_token(bool disable)
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        if (disable) user.Disable(); else user.RevokeSessions();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        // Esta prueba demuestra el contrato HTTP con doble; la persistencia se prueba por separado.
    }

    [Fact]
    public async Task Unavailable_storage_never_grants_access_or_updates_profile()
    {
        await using var host = new IdentityTestHost();
        var user = host.Seed();
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(user));
        host.Users.Unavailable = true;
        using var response = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("STORAGE_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PutAsJsonAsync("/api/users/me/profile", new { firstName = "Changed", lastName = "Name" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = IdentityTestHost.Password })).StatusCode);
        Assert.Equal("Ana", user.FirstName);
        Assert.Equal(0, host.Users.Saves);
    }

    [Fact]
    public async Task Registration_and_login_share_ten_attempts_per_window()
    {
        await using var host = new IdentityTestHost();
        using var client = host.Client();
        for (var i = 0; i < 10; i++)
        {
            using var response = i % 2 == 0
                ? await client.PostAsJsonAsync("/api/users", new { firstName = "Ana" })
                : await client.PostAsJsonAsync("/api/auth/login", new { email = "none@example.com", password = "incorrecta" });
            Assert.Equal(i % 2 == 0 ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "none@example.com", password = "incorrecta" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), limited.Headers.RetryAfter?.Delta);
        using var limitedRegistration = await client.PostAsJsonAsync("/api/users", new { firstName = "Ana" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedRegistration.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), limitedRegistration.Headers.RetryAfter?.Delta);
        // Otro host no hereda los intentos. No hay esperas ni configuración de límites debilitada.
        await using var isolated = new IdentityTestHost();
        using var fresh = isolated.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/auth/login",
            new { email = "none@example.com", password = "incorrecta" })).StatusCode);
    }
}
