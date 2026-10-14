namespace OriSync.Api.StudentQrCodes;

using Microsoft.Extensions.Options;

public sealed partial class StudentQrEmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<StudentQrEmailOptions> options,
    TimeProvider timeProvider,
    ILogger<StudentQrEmailOutboxWorker> logger) : BackgroundService
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

            await Task.Delay(StudentQrEmailConstants.DispatcherPollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<StudentQrEmailOutboxProcessor>()
            .ProcessOneAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "Student QR email dispatcher failed; it will retry.")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "Student QR email dispatcher is disabled.")]
    private static partial void LogDisabled(ILogger logger);
}
