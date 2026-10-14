using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OriSync.Api.Authentication;
using OriSync.Api.MentorManagement;
using OriSync.Api.StudentQrCodes;

namespace OriSync.Api.Tests;

public sealed class EmailSenderTests
{
    private static readonly ResendOptions Options = new()
    {
        ApiKey = "test-api-key",
        FromEmail = "OriSync-No-reply@trainmate.nemasites.com"
    };

    [Fact]
    public async Task MentorInvitationContainsHtmlButtonAndPlainUrlFallback()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendMentorInvitationEmailSender(
            client,
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<ResendMentorInvitationEmailSender>.Instance);

        Assert.True(await sender.SendAsync(
            "mentor@example.com",
            "https://orisync.test/activate#invitation=1&token=secret",
            "invitation/1",
            CancellationToken.None));

        using var payload = JsonDocument.Parse(handler.Body!);
        var root = payload.RootElement;
        Assert.Contains("Activate account", root.GetProperty("html").GetString());
        Assert.Contains("<a href=", root.GetProperty("html").GetString());
        Assert.Contains("https://orisync.test/activate", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task StudentQrEmailContainsPngAttachment()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendStudentQrEmailSender(
            client,
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<ResendStudentQrEmailSender>.Instance);

        Assert.True(await sender.SendAsync(
            "student@example.com",
            "Jane Doe",
            "223456789",
            new StudentQrCodeDocument([1, 2, 3], "223456789-qr.png", "https://orisync.test/qr"),
            "student-qr/1",
            CancellationToken.None));

        using var payload = JsonDocument.Parse(handler.Body!);
        var attachment = payload.RootElement.GetProperty("attachments")[0];
        Assert.Equal("AQID", attachment.GetProperty("content").GetString());
        Assert.Equal("223456789-qr.png", attachment.GetProperty("filename").GetString());
        Assert.Equal("image/png", attachment.GetProperty("content_type").GetString());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
