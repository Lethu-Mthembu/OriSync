using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace OriSync.Api.Authentication;

public interface IPasswordResetEmailSender
{
    Task<bool> SendAsync(
        string recipient,
        string firstName,
        string code,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public string? ApiKey { get; set; }
    public string? FromEmail { get; set; }
}

public sealed partial class ResendPasswordResetEmailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendPasswordResetEmailSender> logger) : IPasswordResetEmailSender
{
    public async Task<bool> SendAsync(
        string recipient,
        string firstName,
        string code,
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
            subject = "Your OriSync password reset code",
            text = $"Hello {firstName},\n\nYour OriSync password reset code is {code}. " +
                "It expires in 5 minutes. If you did not request this, ignore this email."
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

    [LoggerMessage(LogLevel.Warning, "Password-reset email delivery is not configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Resend rejected a password-reset email with status {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Resend password-reset delivery failed.")]
    private static partial void LogDeliveryFailure(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Resend password-reset delivery timed out.")]
    private static partial void LogDeliveryTimeout(ILogger logger, Exception exception);
}
