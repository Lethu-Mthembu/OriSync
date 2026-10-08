using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.Health;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } renderPort)
{
    if (!int.TryParse(renderPort, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException("PORT must be a valid TCP port number.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services
    .AddHealthChecks()
    .AddCheck(
        "application",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<DatabaseReadinessHealthCheck>(
        "database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(10));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<IPasswordHasher<PasswordResetOtp>, PasswordHasher<PasswordResetOtp>>();
builder.Services.AddScoped<OriSync.Api.Authentication.AuthenticationService>();
builder.Services.Configure<PasswordResetOptions>(
    builder.Configuration.GetSection(PasswordResetOptions.SectionName));
builder.Services.Configure<ResendOptions>(
    builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.AddHttpClient<IPasswordResetEmailSender, ResendPasswordResetEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddAuthentication(AuthenticationConstants.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        AuthenticationConstants.Scheme,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = AuthenticationConstants.AntiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.HeaderName = AuthenticationConstants.AntiforgeryHeaderName;
});

var databaseConnection = builder.Configuration.GetConnectionString("OriSync");
builder.Services.AddDbContext<OriSyncDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(databaseConnection))
    {
        options.UseNpgsql();
    }
    else
    {
        options.UseNpgsql(databaseConnection);
    }

    options.UseSnakeCaseNamingConvention();
});
builder.Services.AddDataProtection()
    .SetApplicationName("OriSync")
    .PersistKeysToDbContext<OriSyncDbContext>();

var app = builder.Build();

if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    Environment.ExitCode = await AdminBootstrapCommand.RunAsync(
        app.Services,
        app.Configuration,
        Console.Out);
    return;
}

if (args.Contains("--rotate-admin-recovery", StringComparer.Ordinal))
{
    Environment.ExitCode = await AdminBootstrapCommand.RotateRecoveryCodeAsync(
        app.Services,
        app.Configuration,
        Console.Out);
    return;
}

// Render terminates TLS at its reverse proxy. Trust only the nearest forwarded
// hop so security components still see the original HTTPS request scheme.
// Render's proxy addresses are dynamic, so an IP allow-list is not available.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api/auth"))
    {
        context.Response.Headers.CacheControl = "no-store";
    }

    await next();
});

// Render terminates HTTPS at its reverse proxy and forwards HTTP to the container.
// Local development owns its TLS listener, so redirect only in Development.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
});

app.MapGet("/api/status", (IHostEnvironment environment) => Results.Ok(new
{
    service = "OriSync.Api",
    status = "ready",
    environment = environment.EnvironmentName
}));

app.MapAuthenticationEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString().ToLowerInvariant()
    });
}

public partial class Program;
