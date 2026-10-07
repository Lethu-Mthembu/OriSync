namespace OriSync.Api.Domain;

public sealed class StudentEnrollment
{
    public long Id { get; set; }
    public long OrientationId { get; set; }
    public long StudentId { get; set; }
    public long? CurrentGroupId { get; set; }
    public long? PendingGroupId { get; set; }
    public DateOnly? PendingGroupEffectiveDate { get; set; }
    public int QrVersion { get; set; } = 1;
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }

    public Orientation Orientation { get; set; } = null!;
    public Person Student { get; set; } = null!;
    public OrientationGroup? CurrentGroup { get; set; }
    public OrientationGroup? PendingGroup { get; set; }
    public ICollection<AttendanceRecord> AttendanceRecords { get; } = [];
}
