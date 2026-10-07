using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace OriSync.Api.Authentication;

public static class CredentialSecrets
{
    public static string GenerateOpaqueToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public static bool FixedTimeEquals(string suppliedValue, string expectedHash)
    {
        var suppliedHash = Hash(suppliedValue);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(suppliedHash),
            Convert.FromHexString(expectedHash));
    }
}
