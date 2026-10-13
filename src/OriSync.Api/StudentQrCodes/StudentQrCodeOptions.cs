namespace OriSync.Api.StudentQrCodes;

public sealed class StudentQrCodeOptions
{
    public const string SectionName = "QrCodes";

    public string PublicBaseUrl { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;

    public static bool HasValidPublicBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    public static bool HasValidSigningKey(string? value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
