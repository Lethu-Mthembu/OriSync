namespace OriSync.Api.OrientationManagement;

public sealed record SaveOrientationRequest(
    int Year,
    string? Name,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly AttendanceOpensAt,
    TimeOnly AttendanceClosesAt);

public sealed record SaveOrientationGroupRequest(string Name, string? BadgeColor);

public sealed record OrientationGroupResponse(
    long Id,
    string Name,
    string BadgeColor,
    bool IsActive,
    bool CanDelete);

public sealed record OrientationResponse(
    long Id,
    int Year,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly AttendanceOpensAt,
    TimeOnly AttendanceClosesAt,
    string TimeZoneId,
    bool IsActive,
    DateTimeOffset RetentionDueAt,
    IReadOnlyList<DateOnly> OperatingDates,
    IReadOnlyList<OrientationGroupResponse> Groups);
