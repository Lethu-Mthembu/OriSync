using OriSync.Api.Domain;

namespace OriSync.Api.Authentication;

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RequestMentorPasswordResetRequest(string MentorNumber, string Email);

public sealed record CompleteMentorPasswordResetRequest(
    string MentorNumber,
    string Email,
    string Code,
    string NewPassword);

public sealed record PasswordResetRequestResponse(string Message);

public sealed record RecoverAdminRequest(
    string Email,
    string RecoveryCode,
    string NewPassword);

public sealed record SessionResponse(
    long AccountId,
    string FirstName,
    string Surname,
    string Role,
    long? GroupId,
    bool MustChangePassword,
    DateTimeOffset AbsoluteExpiresAt);

public sealed record CsrfTokenResponse(string RequestToken);

public sealed record AdminRecoveryResponse(string RecoveryCode);

public sealed record LoginResult(AccountSession Session, Account Account, string Token);
