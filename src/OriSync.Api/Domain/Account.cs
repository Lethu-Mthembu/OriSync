namespace OriSync.Api.Domain;

public sealed class Account
{
    public long Id { get; set; }
    public long PersonId { get; set; }
    public AccountRole Role { get; set; }
    public long? CurrentGroupId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? RecoveryCodeHash { get; set; }
    public DateTimeOffset? RecoveryCodeIssuedAt { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? DeletionEligibleAt { get; set; }

    public Person Person { get; set; } = null!;
    public OrientationGroup? CurrentGroup { get; set; }
    public ICollection<AccountSession> Sessions { get; } = [];
    public ICollection<PasswordResetOtp> PasswordResetOtps { get; } = [];
}
