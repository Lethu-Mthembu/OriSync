namespace OriSync.Api.Domain;

public sealed class PasswordResetOtp
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public Account Account { get; set; } = null!;
}
