namespace OriSync.Api.Domain;

public sealed class StudentQrEmailOutbox
{
    public long Id { get; set; }
    public long StudentEnrollmentId { get; set; }
    public int QrVersion { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset DiscardAfter { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DiscardedAt { get; set; }

    public StudentEnrollment StudentEnrollment { get; set; } = null!;
}
