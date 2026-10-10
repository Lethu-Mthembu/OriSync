using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OriSync.Api.Authentication;

namespace OriSync.Api.MentorManagement;

public interface IMentorInvitationEmailSender
{
    Task<bool> SendAsync(
        string recipient,
        string activationLink,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed partial class ResendMentorInvitationEmailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendMentorInvitationEmailSender> logger) : IMentorInvitationEmailSender
{
    public async Task<bool> SendAsync(
        string recipient,
        string activationLink,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) ||
            string.IsNullOrWhiteSpace(settings.FromEmail))
        {
            LogNotConfigured(logger);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new
        {
            from = settings.FromEmail,
            to = new[] { recipient },
            subject = "Activate your OriSync mentor account",
            text = "You have been invited to OriSync as an orientation mentor. " +
                $"Open this secure link to create your account: {activationLink}\n\n" +
                "The link expires 24 hours after this email is delivered. " +
                "If you were not expecting this invitation, ignore this email."
        });

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            LogRejected(logger, (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException exception)
        {
            LogDeliveryFailure(logger, exception);
            return false;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDeliveryTimeout(logger, exception);
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "Mentor-invitation email delivery is not configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Resend rejected a mentor-invitation email with status {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Resend mentor-invitation delivery failed.")]
    private static partial void LogDeliveryFailure(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Resend mentor-invitation delivery timed out.")]
    private static partial void LogDeliveryTimeout(ILogger logger, Exception exception);
}
