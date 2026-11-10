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
            "EMAIL_ALREADY_REGISTERED" or "PROFILE_CONFLICT" or "DNI_ALREADY_REGISTERED" => 409,
            "INVALID_CREDENTIALS" => 401,
            "ACCESS_DENIED" => 403,
            "IDENTITY_NOT_FOUND" or "IDENTITY_MISMATCH" => 422,
            "IDENTITY_PROVIDER_UNAVAILABLE" => 503,
            _ => 400
        };
        if (status == 401) Response.Headers.WWWAuthenticate = "Bearer";
        return Problem(statusCode: status, title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
