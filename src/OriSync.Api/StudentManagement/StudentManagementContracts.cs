using OriSync.Api.Domain;

namespace OriSync.Api.StudentManagement;

public sealed record SaveStudentRequest(
    string StudentNumber,
    string FullNames,
    string Surname,
    string StudentEmail,
    string PersonalEmail,
    StudentAgeGroup AgeGroup,
    Gender Gender,
    Ethnicity Ethnicity);

public sealed record OnboardExistingStudentRequest(string StudentNumber);

public sealed record StudentDirectoryResponse(
    IReadOnlyList<StudentDirectoryItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record StudentDirectoryItemResponse(
    long EnrollmentId,
    string StudentNumber,
    string FullNames,
    string Surname,
    string Initials,
    string StudentEmail,
    string? PersonalEmail,
    StudentAgeGroup AgeGroup,
    Gender Gender,
    Ethnicity Ethnicity,
    long? GroupId,
    string GroupName,
    string GroupBadgeColor,
    string AttendanceStatus,
    bool IsIncomingTransfer,
    DateOnly? GroupEffectiveDate,
    int QrVersion);

public sealed record StudentLookupResponse(
    string Match,
    string StudentNumber,
    string? FullNames,
    string? Surname,
    string? StudentEmail,
    string? GroupName,
    string? Message);

public sealed record StudentMutationResponse(
    StudentDirectoryItemResponse Student,
    string Outcome);
