namespace OriSync.Api.MentorManagement;

public sealed record CreateMentorInvitationRequest(string Email, long GroupId);

public sealed record ChangeMentorInvitationGroupRequest(long GroupId);

public sealed record AcceptMentorInvitationRequest(
    long InvitationId,
    string Token,
    string FirstName,
    string Surname,
    string MentorNumber,
    string Password);

public sealed record ValidateMentorInvitationRequest(long InvitationId, string Token);

public sealed record UpdateMentorRequest(
    string FirstName,
    string Surname,
    string MentorNumber,
    string Email,
    long GroupId);

public sealed record MentorInvitationValidationResponse(
    string Email,
    long GroupId,
    string GroupName,
    string GroupBadgeColor,
    DateTimeOffset ExpiresAt);

public sealed record MentorDirectoryItemResponse(
    long Id,
    string RecordType,
    string Status,
    string Email,
    string? MentorNumber,
    string? FirstName,
    string? Surname,
    long GroupId,
    string GroupName,
    string GroupBadgeColor,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletionEligibleAt,
    bool CanDelete);

public sealed record MentorDirectoryResponse(
    IReadOnlyList<MentorDirectoryItemResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
