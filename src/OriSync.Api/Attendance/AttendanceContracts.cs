using OriSync.Api.Domain;

namespace OriSync.Api.Attendance;

public sealed record ScanAttendanceRequest(string DecodedUrl);

public sealed record CorrectAttendanceRequest(AttendanceStatus Status);

public sealed record AttendanceMutationResponse(
    string Outcome,
    long EnrollmentId,
    string StudentNumber,
    string FullNames,
    string Surname,
    AttendanceStatus Status,
    string Message);
