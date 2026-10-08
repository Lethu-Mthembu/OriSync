using Microsoft.Extensions.Options;

namespace OriSync.Api.Authentication;

public sealed partial class PasswordResetEmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PasswordResetOptions> options,
    TimeProvider timeProvider,
    ILogger<PasswordResetEmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DispatcherEnabled)
        {
            LogDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await ProcessOneAsync(stoppingToken))
                {
                    // Drain all currently available rows before sleeping.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatchFailure(logger, exception);
            }

            await Task.Delay(
                PasswordResetConstants.DispatcherPollInterval,
                timeProvider,
                stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider
            .GetRequiredService<PasswordResetEmailOutboxProcessor>();
        return await processor.ProcessOneAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Information, "Password-reset email dispatcher is disabled.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Password-reset email dispatcher failed; it will retry.")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);
}
