namespace OriSync.Api.Domain;

public sealed class PersonEmail
{
    public long Id { get; set; }
    public long PersonId { get; set; }
    public EmailType EmailType { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public Person Person { get; set; } = null!;
}
