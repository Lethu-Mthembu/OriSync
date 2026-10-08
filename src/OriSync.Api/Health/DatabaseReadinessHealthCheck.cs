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

            var pendingMigrations = await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                LogPendingDatabaseMigrations();
                return HealthCheckResult.Unhealthy("Database schema is not current.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            // The public response stays generic because provider exceptions can
            // contain credentials. Log only the exception type; never serialize the
            // provider message or connection string, even to private service logs.
            LogDatabaseReadinessFailure(exception.GetType().Name);
            return HealthCheckResult.Unhealthy("Database connection failed.");
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Database readiness check failed with {ExceptionType}.")]
    private partial void LogDatabaseReadinessFailure(string exceptionType);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Database readiness check found pending migrations.")]
    private partial void LogPendingDatabaseMigrations();
}
