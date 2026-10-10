namespace OriSync.Api.Authentication;

public static class PasswordRules
{
    public const int MinimumLength = 8;

    public static bool IsValid(string? password) =>
        password is { Length: >= MinimumLength } &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(character => !char.IsLetterOrDigit(character));

    // Bootstrap passwords are temporary and must be replaced immediately.
    // Keeping this separate allows a one-time operational password without
    // weakening the permanent password policy.
    public static bool IsValidTemporary(string? password) =>
        password is { Length: >= MinimumLength };

    public const string Requirements =
        "Passwords must contain at least eight characters, including an uppercase letter, " +
        "a lowercase letter and a special character.";
}
