using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace OriSync.Api.StudentQrCodes;

public sealed class StudentQrCodeTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _signingKey;
    private readonly Uri _scanUri;

    public StudentQrCodeTokenService(IOptions<StudentQrCodeOptions> options)
    {
        var values = options.Value;
        if (!StudentQrCodeOptions.HasValidPublicBaseUrl(values.PublicBaseUrl))
        {
            throw new InvalidOperationException(
                "QrCodes:PublicBaseUrl must be an HTTPS origin without credentials, a path, a query or a fragment.");
        }

        if (!StudentQrCodeOptions.HasValidSigningKey(values.SigningKey))
        {
            throw new InvalidOperationException(
                "QrCodes:SigningKey must be a Base64 value containing at least 32 bytes.");
        }

        _signingKey = Convert.FromBase64String(values.SigningKey);
        _scanUri = new Uri($"{values.PublicBaseUrl.TrimEnd('/')}/scan", UriKind.Absolute);
    }

    public StudentQrCodeToken Create(StudentQrCodePayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadPart = WebEncoders.Base64UrlEncode(json);
        var signaturePart = WebEncoders.Base64UrlEncode(
            HMACSHA256.HashData(_signingKey, json));
        return new StudentQrCodeToken(
            $"{_scanUri.AbsoluteUri}#payload={payloadPart}&signature={signaturePart}",
            payload);
    }

    public bool TryValidate(string url, out StudentQrCodePayload? payload)
    {
        payload = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.GetLeftPart(UriPartial.Path), _scanUri.AbsoluteUri, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        try
        {
            var parts = uri.Fragment[1..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2)
                .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
            if (!parts.TryGetValue("payload", out var encodedPayload) ||
                !parts.TryGetValue("signature", out var encodedSignature))
            {
                return false;
            }

            var json = WebEncoders.Base64UrlDecode(encodedPayload);
            var suppliedSignature = WebEncoders.Base64UrlDecode(encodedSignature);
            var expectedSignature = HMACSHA256.HashData(_signingKey, json);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                return false;
            }

            payload = JsonSerializer.Deserialize<StudentQrCodePayload>(json, JsonOptions);
            return payload is not null &&
                payload.Version > 0 &&
                !string.IsNullOrWhiteSpace(payload.StudentNumber) &&
                !string.IsNullOrWhiteSpace(payload.FullNames) &&
                !string.IsNullOrWhiteSpace(payload.Surname) &&
                !string.IsNullOrWhiteSpace(payload.StudentEmail);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
