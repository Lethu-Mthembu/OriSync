namespace OriSync.Api.Authentication;

public static class PasswordRules
{
    public const int MinimumLength = 8;

    public static bool IsValid(string? password) =>
        password is { Length: >= MinimumLength };
}
