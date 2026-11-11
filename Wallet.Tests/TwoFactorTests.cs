using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wallet.Application;
using Wallet.Domain;
using Xunit;

namespace Wallet.Tests;

public sealed class TwoFactorTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static (IdentityTestHost Host, WebApplicationFactory<Program> App) CreateApp()
    {
        var host = new IdentityTestHost();
        var app = host.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(new FixedClock(Now));
        }));
        return (host, app);
    }

    private static HttpClient Client(WebApplicationFactory<Program> app, string? token = null)
    {
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Code(string secret, int offset) => Totp.CodeAt(Base32.Decode(secret), Totp.StepAt(Now) + offset);

    private static async Task<string> EnableAsync(HttpClient client)
    {
        var setup = await client.PostAsync("/api/v1/me/two-factor/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!.Secret;
        var confirm = await client.PostAsJsonAsync("/api/v1/me/two-factor/confirm", new { code = Code(secret, -1) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        return secret;
    }

    private static async Task<string> ProblemCode(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Totp_matches_rfc6238_test_vectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, Totp.CodeAt(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Base32_encodes_like_authenticator_apps_expect()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Base32.Encode(secret));
        Assert.Equal(secret, Base32.Decode("gezdgnbvgy3tqojqgezdgnbvgy3tqojq"));
    }

    [Fact]
    public void Domain_rejects_reused_steps_and_disabling_clears_everything()
    {
        var user = User.Create("Ana", "Pérez", "ana@example.com", Now);
        user.BeginTwoFactorSetup("v1.cifrado");
        user.EnableTwoFactor(100, Now);
        Assert.False(user.TryUseTwoFactorStep(100));
        Assert.True(user.TryUseTwoFactorStep(101));
        Assert.Throws<InvalidOperationException>(() => user.BeginTwoFactorSetup("v1.otro"));
        user.DisableTwoFactor();
        Assert.False(user.IsTwoFactorEnabled);
        Assert.Null(user.TwoFactorSecret);
    }

    [Fact]
    public async Task Login_without_two_factor_keeps_the_same_response()
    {
        var (host, app) = CreateApp();
        await using var _ = host;
        host.Seed();
        var login = await Client(app).PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "ana@example.com", Password = IdentityTestHost.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("twoFactor", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(JsonSerializer.Deserialize<TokenResponse>(body, JsonSerializerOptions.Web)!.AccessToken));
    }

    [Fact]
    public async Task Setup_stores_the_secret_encrypted_and_requires_a_valid_code()
    {
        var (host, app) = CreateApp();
        await using var _ = host;
        var user = host.Seed();
        using var client = Client(app, host.Token(user));

        var setup = await client.PostAsync("/api/v1/me/two-factor/setup", null);
        var response = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!;
        Assert.StartsWith("otpauth://totp/padi:ana%40example.com?secret=", response.OtpAuthUri);
        Assert.StartsWith("v1.", user.TwoFactorSecret);
        Assert.DoesNotContain(response.Secret, user.TwoFactorSecret!);
        Assert.False(user.IsTwoFactorEnabled);

        var wrong = await client.PostAsJsonAsync("/api/v1/me/two-factor/confirm", new { code = "000000" == Code(response.Secret, 0) ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("TWO_FACTOR_CODE_INVALID", await ProblemCode(wrong));

        var confirm = await client.PostAsJsonAsync("/api/v1/me/two-factor/confirm", new { code = Code(response.Secret, 0) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.True((await confirm.Content.ReadFromJsonAsync<TwoFactorStatusResponse>())!.Enabled);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/v1/me/two-factor/setup", null)).StatusCode);
    }

    [Fact]
    public async Task Login_with_two_factor_requires_the_code_and_rejects_reuse()
    {
        var (host, app) = CreateApp();
        await using var _ = host;
        var user = host.Seed();
        var secret = await EnableAsync(Client(app, host.Token(user)));
        using var anonymous = Client(app);

        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "ana@example.com", Password = IdentityTestHost.Password });
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        var challenge = (await login.Content.ReadFromJsonAsync<TwoFactorChallengeResponse>())!;
        Assert.True(challenge.TwoFactorRequired);
        Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync());

        var wrong = await anonymous.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { challengeToken = challenge.ChallengeToken, code = Code(secret, -1) });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("TWO_FACTOR_LOGIN_FAILED", await ProblemCode(wrong));

        var ok = await anonymous.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { challengeToken = challenge.ChallengeToken, code = Code(secret, 0) });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var token = (await ok.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        Assert.Equal(HttpStatusCode.OK, (await Client(app, token).GetAsync("/api/v1/me/profile")).StatusCode);

        var reused = await anonymous.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { challengeToken = challenge.ChallengeToken, code = Code(secret, 0) });
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Tampered_or_stale_challenges_are_rejected_and_disable_restores_normal_login()
    {
        var (host, app) = CreateApp();
        await using var _ = host;
        var user = host.Seed();
        using var client = Client(app, host.Token(user));
        var secret = await EnableAsync(client);
        using var anonymous = Client(app);

        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "ana@example.com", Password = IdentityTestHost.Password });
        var challenge = (await login.Content.ReadFromJsonAsync<TwoFactorChallengeResponse>())!.ChallengeToken;
        var tampered = await anonymous.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { challengeToken = challenge[..^2] + "AA", code = Code(secret, 0) });
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        Assert.Equal("TWO_FACTOR_CHALLENGE_EXPIRED", await ProblemCode(tampered));

        var disabled = await client.PostAsJsonAsync("/api/v1/me/two-factor/disable", new { code = Code(secret, 0) });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await disabled.Content.ReadFromJsonAsync<TwoFactorStatusResponse>())!.Enabled);

        var stale = await anonymous.PostAsJsonAsync("/api/v1/auth/login/two-factor", new { challengeToken = challenge, code = Code(secret, 1) });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        var normal = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "ana@example.com", Password = IdentityTestHost.Password });
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
    }

    [Fact]
    public async Task Two_factor_endpoints_require_a_signed_in_client()
    {
        var (host, app) = CreateApp();
        await using var _ = host;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(app).GetAsync("/api/v1/me/two-factor")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(app).PostAsync("/api/v1/me/two-factor/setup", null)).StatusCode);
    }
}
