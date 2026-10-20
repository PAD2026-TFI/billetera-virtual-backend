using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Wallet.Application;
using Wallet.Domain;
namespace Wallet.Api.Security;

public sealed class JwtTokenService(JwtSettings settings, TimeProvider clock) : ITokenService
{
    public TokenResponse Create(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", user.Role),
            new("session_version", user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            now, now.AddMinutes(settings.LifetimeMinutes),
            new SigningCredentials(new SymmetricSecurityKey(settings.GetKey()), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", settings.LifetimeMinutes * 60);
    }
}
