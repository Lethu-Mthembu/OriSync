using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } renderPort)
{
    if (!int.TryParse(renderPort, out var port) || port is < 1 or > 65535)
    {
        throw new InvalidOperationException("PORT must be a valid TCP port number.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.AddHealthChecks();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLowerInvariant()
        });
    }
});

app.MapGet("/api/status", (IHostEnvironment environment) => Results.Ok(new
{
    service = "OriSync.Api",
    status = "ready",
    environment = environment.EnvironmentName
}));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
