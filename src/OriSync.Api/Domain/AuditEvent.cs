namespace OriSync.Api.Domain;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public long? AccountId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Account? Account { get; set; }
}
