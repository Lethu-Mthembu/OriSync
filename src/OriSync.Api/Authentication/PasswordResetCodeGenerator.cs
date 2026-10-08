using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace OriSync.Api.Authentication;

public interface IPasswordResetCodeGenerator
{
    bool TryCreate(out string nonce, out string code);
    bool TryRegenerate(string nonce, out string code);
}

public sealed class PasswordResetCodeGenerator(IOptions<PasswordResetOptions> options)
    : IPasswordResetCodeGenerator
{
    public bool TryCreate(out string nonce, out string code)
    {
        nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        if (TryRegenerate(nonce, out code))
        {
            return true;
        }

        nonce = string.Empty;
        return false;
    }

    public bool TryRegenerate(string nonce, out string code)
    {
        code = string.Empty;
        var settings = options.Value;
        if (settings.CodeLength is < 4 or > 9 ||
            !TryDecodeSecret(settings.CodeSecret, out var secret))
        {
            return false;
        }

        byte[] nonceBytes;
        try
        {
            nonceBytes = WebEncoders.Base64UrlDecode(nonce);
        }
        catch (FormatException)
        {
            return false;
        }

        var digest = HMACSHA256.HashData(secret, nonceBytes);
        var numericValue = BinaryPrimitives.ReadUInt64BigEndian(digest);
        var modulus = (ulong)Math.Pow(10, settings.CodeLength);
        code = (numericValue % modulus)
            .ToString($"D{settings.CodeLength}", CultureInfo.InvariantCulture);
        return true;
    }

    public static bool HasValidSecret(string? value) =>
        TryDecodeSecret(value, out _);

    private static bool TryDecodeSecret(string? value, out byte[] secret)
    {
        secret = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            secret = Convert.FromBase64String(value);
            return secret.Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
