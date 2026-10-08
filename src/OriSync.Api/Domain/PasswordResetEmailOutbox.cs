namespace OriSync.Api.Domain;

public sealed class PasswordResetEmailOutbox
{
    public long Id { get; set; }
    public long PasswordResetOtpId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string RecipientFirstName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset DiscardAfter { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DiscardedAt { get; set; }

    public PasswordResetOtp PasswordResetOtp { get; set; } = null!;
}
