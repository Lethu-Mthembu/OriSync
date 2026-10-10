using Microsoft.Extensions.Options;

namespace OriSync.Api.MentorManagement;

public sealed partial class MentorInvitationEmailWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MentorInvitationOptions> options,
    TimeProvider timeProvider,
    ILogger<MentorInvitationEmailWorker> logger) : BackgroundService
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
                    // Drain all available invitations before sleeping.
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

            await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<MentorInvitationEmailProcessor>()
            .ProcessOneAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Information, "Mentor-invitation email dispatcher is disabled.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Mentor-invitation email dispatcher failed; it will retry.")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);
}
