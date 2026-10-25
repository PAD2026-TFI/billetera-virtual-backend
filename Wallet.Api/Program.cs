using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Wallet.Api.Errors;
using Wallet.Api.Security;
using Wallet.Application;
using Wallet.Infrastructure;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion =
        Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;

    options.AddDocumentTransformer<SecurityDocumentTransformer>();
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
    o.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(x => x.Key, _ => new[] { "Campo ausente, inválido o no permitido." });
        var problem = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(errors)
        { Status = 400, Title = "Entrada inválida" };
        problem.Extensions["code"] = "INVALID_INPUT";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        var response = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problem);
        response.ContentTypes.Add("application/problem+json");
        return response;
    });
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, CurrentUser>();


builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: 429, title: "Demasiados intentos",
            extensions: new Dictionary<string, object?> { ["code"] = "RATE_LIMITED" }).ExecuteAsync(context.HttpContext);
    };
});
var enabled = builder.Configuration.GetValue<bool>("Identity:Enabled");
Console.WriteLine("=================================");
Console.WriteLine($"IDENTITY ENABLED = {enabled}");
Console.WriteLine("=================================");

if (enabled)
{
    var connection =
        builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connection))
        throw new InvalidOperationException(
            "Falta ConnectionStrings:DefaultConnection.");

    var jwt = builder.Configuration
        .GetSection("Jwt")
        .Get<JwtSettings>() ?? new();

    var key = jwt.GetKey();

    builder.Services.AddScoped<IdentityService>();
    builder.Services.AddScoped<WalletService>();
    builder.Services.AddSingleton(jwt);
    builder.Services.AddScoped<ITokenService, JwtTokenService>();

    builder.Services.AddInfrastructure(connection);

    builder.Services.AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.IncludeErrorDetails = false;

            o.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,

                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(key),

                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,

                    ValidAlgorithms =
                        [SecurityAlgorithms.HmacSha256],

                    ClockSkew =
                        TimeSpan.FromSeconds(30),

                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };

            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!Guid.TryParse(
                            context.Principal?
                                .FindFirst("sub")?.Value,
                            out var id)
                        || id == Guid.Empty
                        || !Guid.TryParse(
                            context.Principal?.FindFirst("jti")?.Value,
                            out var tokenId)
                        || tokenId == Guid.Empty
                        ||
                        !int.TryParse(
                            context.Principal?
                                .FindFirst("session_version")?.Value,
                            out var version)
                        || version < 0)
                    {
                        context.Fail("Invalid token");
                        return;
                    }

                    var users = context.HttpContext
                        .RequestServices
                        .GetRequiredService<IUserRepository>();

                    var user =
                        await users.FindByIdAsync(
                            id,
                            context.HttpContext.RequestAborted);

                    if (user is null ||
                        !user.IsActive ||
                        user.SessionVersion != version ||
                        user.Role !=
                        context.Principal?
                            .FindFirst("role")?.Value)
                    {
                        context.Fail("Invalid session");
                    }
                }
            };
        });

    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

        o.AddPolicy("Client", p =>
            p.RequireAuthenticatedUser()
             .RequireRole("Client"));
    });
}


var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Wallet API v1");

        options.RoutePrefix = "swagger";
    });
}
else
{
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseStatusCodePages(async context =>
{
    await Results.Problem(
        statusCode: context.HttpContext.Response.StatusCode,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = $"HTTP_{context.HttpContext.Response.StatusCode}"
        })
        .ExecuteAsync(context.HttpContext);
});

app.UseRouting();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";

    if (!enabled && context.Request.Path.StartsWithSegments("/api"))
    {
        await Results.Problem(
            statusCode: 503,
            title: "Identidad pendiente de configuración",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "IDENTITY_NOT_CONFIGURED"
            })
            .ExecuteAsync(context);

        return;
    }

    await next(context);
});

app.UseRateLimiter();

if (enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapControllers();

app.Run();

public partial class Program { }