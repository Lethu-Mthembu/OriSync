namespace OriSync.Api.Domain;

public sealed class AttendanceRecord
{
    public long Id { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long OrientationId { get; set; }
    public long GroupId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Absent;
    public AttendanceCaptureMethod CaptureMethod { get; set; } = AttendanceCaptureMethod.Automatic;
    public long? MarkedByAccountId { get; set; }

    public StudentEnrollment StudentEnrollment { get; set; } = null!;
    public OrientationGroup Group { get; set; } = null!;
    public Account? MarkedByAccount { get; set; }
}
