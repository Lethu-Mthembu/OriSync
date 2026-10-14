using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OriSync.Api.Authentication;

namespace OriSync.Api.StudentQrCodes;

public interface IStudentQrEmailSender
{
    Task<bool> SendAsync(
        string recipient,
        string studentName,
        string studentNumber,
        StudentQrCodeDocument document,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed partial class ResendStudentQrEmailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> options,
    ILogger<ResendStudentQrEmailSender> logger) : IStudentQrEmailSender
{
    public async Task<bool> SendAsync(
        string recipient,
        string studentName,
        string studentNumber,
        StudentQrCodeDocument document,
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

        var encodedName = WebUtility.HtmlEncode(studentName);
        var encodedNumber = WebUtility.HtmlEncode(studentNumber);
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new
        {
            from = settings.FromEmail,
            to = new[] { recipient },
            subject = "Your OriSync attendance QR code",
            html = $"""
                <p>Hello {encodedName},</p>
                <p>Your OriSync attendance QR code is attached to this email.</p>
                <p><strong>Student number:</strong> {encodedNumber}</p>
                <p>Keep the attached PNG available for attendance scanning throughout orientation. If your student details change, use only the replacement QR code sent afterwards.</p>
                """,
            text = $"Hello {studentName},\n\nYour OriSync attendance QR code is attached. " +
                $"Student number: {studentNumber}. Keep this PNG available throughout orientation. " +
                "If your student details change, use only the replacement QR code sent afterwards.",
            attachments = new[]
            {
                new
                {
                    content = Convert.ToBase64String(document.Content),
                    filename = document.FileName,
                    content_type = "image/png"
                }
            }
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

    [LoggerMessage(LogLevel.Warning, "Student QR email delivery is not configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Resend rejected a student QR email with status {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, int statusCode);

    [LoggerMessage(LogLevel.Warning, "Resend student QR email delivery failed.")]
    private static partial void LogDeliveryFailure(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Resend student QR email delivery timed out.")]
    private static partial void LogDeliveryTimeout(ILogger logger, Exception exception);
}
