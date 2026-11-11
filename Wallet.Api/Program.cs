using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
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

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var allowAllOrigins = builder.Environment.IsDevelopment()
    || configuredOrigins == null
    || configuredOrigins.Length == 0
    || configuredOrigins.Contains("*");

builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
{
    if (allowAllOrigins)
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Idempotency-Replayed", "Location");
    }
    else
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http://localhost:5173",
            "https://localhost:5173",
            "http://127.0.0.1:5173",
            "https://127.0.0.1:5173",
            "https://padi-billetera.netlify.app"
        };
        if (configuredOrigins != null)
        {
            foreach (var origin in configuredOrigins)
            {
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    origins.Add(origin.TrimEnd('/'));
                }
            }
        }
        var frontendUrl = builder.Configuration["FrontendUrl"] ?? builder.Configuration["BaseUrl"];
        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            origins.Add(frontendUrl.TrimEnd('/'));
        }

        policy.WithOrigins(origins.ToArray())
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Idempotency-Replayed", "Location");
    }
}));

builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("padi-card-payments", context => RateLimitPartition.GetFixedWindowLimiter(
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
    var mail = builder.Configuration.GetSection("Mail").Get<MailOptions>() ?? new();
    if (mail.Enabled && (string.IsNullOrWhiteSpace(mail.Host) || mail.Port is < 1 or > 65535 ||
        !mail.EnableSsl || !System.Net.Mail.MailAddress.TryCreate(mail.Username, out _) || string.IsNullOrWhiteSpace(mail.Password)))
        throw new InvalidOperationException("Falta configurar el envío de correo con TLS.");

    builder.Services.AddSingleton(mail);
    builder.Services.AddSingleton(new EmailVerificationSettings(mail.Enabled, key));
    builder.Services.AddScoped<EmailVerificationService>();

    var checkoutBaseUrl = builder.Configuration["FrontendUrl"]
        ?? builder.Configuration["BaseUrl"]
        ?? builder.Configuration["Checkout:BaseUrl"]
        ?? "https://localhost:7081";

    builder.Services.AddSingleton(TwoFactorSettings.FromMasterKey(key));
    builder.Services.AddScoped<TwoFactorService>();
    builder.Services.AddScoped<IdentityService>();
    builder.Services.AddScoped<WalletService>();
    builder.Services.AddScoped<GatewayService>();
    builder.Services.AddScoped<MerchantIntegrationService>();
    builder.Services.AddScoped<CardSimulatorService>();
    builder.Services.AddScoped<PaymentLinkService>(sp => ActivatorUtilities.CreateInstance<PaymentLinkService>(
        sp,
        checkoutBaseUrl));
    builder.Services.AddScoped<ICheckoutService>(sp => sp.GetRequiredService<PaymentLinkService>());
    builder.Services.AddScoped<ICardService, CardService>();
    builder.Services.AddSingleton(new VirtualCardCredentials(builder.Configuration["VirtualCards:Key"]));
    builder.Services.AddScoped<VirtualCardService>();
    builder.Services.AddScoped<DevelopmentFundingService>();
    builder.Services.AddDataProtection();
    builder.Services.AddScoped<IMovementCursorCodec, MovementCursorCodec>();
    builder.Services.AddScoped<MovementHistoryService>();
    builder.Services.AddScoped<TransferService>();
    builder.Services.AddSingleton(jwt);
    builder.Services.AddScoped<ITokenService, JwtTokenService>();

    builder.Services.Configure<Wallet.Infrastructure.OpenRenaper.OpenRenaperOptions>(
        builder.Configuration.GetSection(Wallet.Infrastructure.OpenRenaper.OpenRenaperOptions.SectionName));
    builder.Services.Configure<IdentityVerificationOptions>(
        builder.Configuration.GetSection(IdentityVerificationOptions.SectionName));
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityVerificationOptions>>().Value);

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
        })
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.SchemeName, null);

    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

        o.AddPolicy("Client", p =>
            p.RequireAuthenticatedUser()
             .RequireRole("Client"));

        o.AddPolicy("Admin", p =>
            p.RequireAuthenticatedUser()
             .RequireRole("Admin"));

        o.AddPolicy("PaymentIntegration", p =>
            p.AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
             .RequireAuthenticatedUser()
             .RequireRole("ExternalClient", "MerchantIntegration"));

        o.AddPolicy("ExternalClient", p =>
            p.AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
             .RequireAuthenticatedUser()
             .RequireRole("ExternalClient"));
    });
}

var app = builder.Build();

app.UseExceptionHandler();

// Documentation is opt-in for local development. Hosting with Development by mistake
// must not publish Swagger or the full OpenAPI catalog by default.
var documentationEnabled = app.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Documentation:Enabled");
if (documentationEnabled)
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Wallet API v1");
        options.RoutePrefix = "swagger";
        options.InjectJavascript("/swagger/wallet-tools.js");
    });
}
else
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/openapi"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    });
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
app.UseStaticFiles();

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
app.UseCors();

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

if (documentationEnabled)
{
    app.MapOpenApi().AllowAnonymous();
    app.MapGet("/swagger/wallet-tools.js", () => Results.Stream(
        typeof(Program).Assembly.GetManifestResourceStream("Wallet.Api.Swagger.wallet-tools.js")!,
        "application/javascript")).AllowAnonymous().ExcludeFromDescription();
}

app.MapGet("/", () => Results.Redirect(documentationEnabled ? "/swagger" : "/health/live")).AllowAnonymous().ExcludeFromDescription();
app.MapControllers();

app.Run();

public partial class Program { }
