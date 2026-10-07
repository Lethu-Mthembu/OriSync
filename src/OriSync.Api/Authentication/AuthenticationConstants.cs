namespace OriSync.Api.Authentication;

public static class AuthenticationConstants
{
    public const string Scheme = "OriSyncSession";
    public const string SessionCookieName = "__Host-OriSync.Session";
    public const string AntiforgeryCookieName = "__Host-OriSync.Antiforgery";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    public const string SessionIdClaim = "orisync_session_id";
    public const string GroupIdClaim = "orisync_group_id";
    public const string MustChangePasswordClaim = "orisync_must_change_password";

    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteSessionLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan AuditLifetime = TimeSpan.FromDays(14);
}
