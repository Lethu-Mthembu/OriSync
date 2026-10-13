namespace OriSync.Api.StudentQrCodes;

public sealed record StudentQrCodePayload(
    string StudentNumber,
    string FullNames,
    string Surname,
    string StudentEmail,
    int Version);

public sealed record StudentQrCodeToken(
    string Url,
    StudentQrCodePayload Payload);

public sealed record StudentQrCodeDocument(
    byte[] Content,
    string FileName,
    string Url);
