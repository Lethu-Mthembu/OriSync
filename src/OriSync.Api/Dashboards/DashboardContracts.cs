namespace OriSync.Api.Dashboards;

public sealed record DashboardMetrics(
    int TotalStudents,
    int PresentToday,
    int AbsentToday,
    decimal AttendanceRate);

public sealed record GroupStudentCountResponse(
    long GroupId,
    string GroupName,
    string BadgeColor,
    int StudentCount,
    int Rank);

public sealed record AdminDashboardResponse(
    bool HasActiveOrientation,
    string? OrientationName,
    DateOnly? AttendanceDate,
    DashboardMetrics Metrics,
    IReadOnlyList<GroupStudentCountResponse> GroupRanking);

public sealed record MentorDashboardResponse(
    string OrientationName,
    DateOnly AttendanceDate,
    long GroupId,
    string GroupName,
    string BadgeColor,
    DashboardMetrics Metrics);
