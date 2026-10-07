namespace OriSync.Api.Domain;

public sealed class AccountSession
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public Account Account { get; set; } = null!;
}
