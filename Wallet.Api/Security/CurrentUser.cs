using Wallet.Application;
namespace Wallet.Api.Security;
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id => Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id)
        ? id : throw new UnauthorizedAccessException();
}
