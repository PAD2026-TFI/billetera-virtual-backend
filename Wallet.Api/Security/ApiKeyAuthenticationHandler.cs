using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Wallet.Application;

namespace Wallet.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IExternalClientRepository clients) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? apiKey = null;

        if (Request.Headers.TryGetValue(HeaderName, out var headerValues) && !string.IsNullOrWhiteSpace(headerValues))
        {
            apiKey = headerValues.ToString();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var headerStr = authHeader.ToString();
            if (headerStr.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = headerStr["ApiKey ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var hash = GatewayService.HashApiKey(apiKey);
            var client = await clients.FindByApiKeyHashAsync(hash, Context.RequestAborted);

            if (client is null || !client.IsActive)
            {
                return AuthenticateResult.Fail("API Key inválida o cliente inactivo.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, client.Id.ToString()),
                new Claim("sub", client.Id.ToString()),
                new Claim(ClaimTypes.Role, "ExternalClient"),
                new Claim("role", "ExternalClient"),
                new Claim("client_name", client.CompanyName),
                new Claim("client_email", client.ContactEmail)
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail(ex.Message);
        }
    }
}
