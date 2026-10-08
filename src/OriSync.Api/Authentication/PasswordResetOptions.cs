namespace OriSync.Api.Authentication;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int CodeLength { get; set; }
}
