namespace OriSync.Api.Domain;

public sealed class Orientation
{
    public long Id { get; set; }
    public int Year { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly AttendanceOpensAt { get; set; }
    public TimeOnly AttendanceClosesAt { get; set; }
    public string TimeZoneId { get; set; } = "Africa/Johannesburg";
    public DateTimeOffset RetentionDueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PurgedAt { get; set; }

    public ICollection<OrientationGroup> Groups { get; } = [];
    public ICollection<StudentEnrollment> StudentEnrollments { get; } = [];
}
