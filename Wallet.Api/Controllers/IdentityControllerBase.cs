using Microsoft.AspNetCore.Mvc;
using Wallet.Application;
namespace Wallet.Api.Controllers;
public abstract class IdentityControllerBase : ControllerBase
{
    protected ActionResult<T> FromResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var error = result.Error!;
        var status = error.Code switch
        {
            "EMAIL_ALREADY_REGISTERED" or "PROFILE_CONFLICT" => 409,
            "TWO_FACTOR_ALREADY_ENABLED" or "TWO_FACTOR_NOT_ENABLED" or "TWO_FACTOR_SETUP_REQUIRED" or "TWO_FACTOR_CONFLICT" => 409,
            "INVALID_CREDENTIALS" or "TWO_FACTOR_CHALLENGE_EXPIRED" or "TWO_FACTOR_LOGIN_FAILED" => 401,
            "TWO_FACTOR_UNAVAILABLE" => 503,
            "ACCESS_DENIED" => 403,
            _ => 400
        };
        if (status == 401) Response.Headers.WWWAuthenticate = "Bearer";
        return Problem(statusCode: status, title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
