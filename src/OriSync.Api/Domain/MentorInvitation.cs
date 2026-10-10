namespace OriSync.Api.Domain;

public sealed class MentorInvitation
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string? ProtectedToken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset DeliveryDiscardAfter { get; set; }
    public int DeliveryVersion { get; set; } = 1;
    public int AttemptCount { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? DeliveryFailedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? PurgeAfter { get; set; }

    public OrientationGroup Group { get; set; } = null!;
}
