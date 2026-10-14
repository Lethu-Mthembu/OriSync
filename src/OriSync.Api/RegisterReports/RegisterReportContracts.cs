using OriSync.Api.Domain;

namespace OriSync.Api.RegisterReports;

public enum RegisterFilter
{
    All,
    Present,
    Absent
}

public sealed record RegisterSummaryResponse(
    int RowCount,
    int GroupCount,
    RegisterFilter AttendanceStatus);

public sealed record RegisterGroupSummaryResponse(
    long GroupId,
    string GroupName,
    string BadgeColor,
    int TotalStudents,
    int PresentStudents,
    int AbsentStudents,
    int MatchingStudents);

public sealed record RegisterDocument(
    byte[] Content,
    string ContentType,
    string FileName);

public sealed record RegisterRow(
    string InitialsAndSurname,
    string StudentNumber,
    StudentAgeGroup AgeGroup,
    Gender Gender,
    Ethnicity Ethnicity,
    AttendanceStatus AttendanceStatus);
