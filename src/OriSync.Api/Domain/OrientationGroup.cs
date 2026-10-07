namespace OriSync.Api.Domain;

public sealed class OrientationGroup
{
    public long Id { get; set; }
    public long OrientationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public Orientation Orientation { get; set; } = null!;
}
