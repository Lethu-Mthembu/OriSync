namespace OriSync.Api.Domain;

public sealed class OrientationGroup
{
    public long Id { get; set; }
    public long OrientationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = "#64748B";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }

    public Orientation Orientation { get; set; } = null!;
}
