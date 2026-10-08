namespace OriSync.Api.Authentication;

public static class PasswordResetConstants
{
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RequestWindow = TimeSpan.FromHours(1);
    public const int MaximumVerificationAttempts = 5;
    public const int MaximumRequestsPerWindow = 5;
}
