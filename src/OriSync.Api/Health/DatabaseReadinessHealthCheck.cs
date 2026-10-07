using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OriSync.Api.Data;

namespace OriSync.Api.Health;

public sealed partial class DatabaseReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DatabaseReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("OriSync")))
        {
            return HealthCheckResult.Unhealthy("Database connection is not configured.");
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();

            // CanConnectAsync intentionally converts provider exceptions into false,
            // which prevents private logs from explaining configuration failures.
            // Opening the connection preserves those diagnostics while the public
            // response remains deliberately generic.
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.CloseConnectionAsync();

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            // The public response stays generic because provider exceptions can
            // contain infrastructure details. Full diagnostics remain in private logs.
            LogDatabaseReadinessFailure(exception);
            return HealthCheckResult.Unhealthy("Database connection failed.");
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Database readiness check failed.")]
    private partial void LogDatabaseReadinessFailure(Exception exception);
}
