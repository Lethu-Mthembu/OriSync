namespace OriSync.Api.Domain;

public sealed class Person
{
    public long Id { get; set; }
    public PersonType PersonType { get; set; }
    public string? InstitutionNumber { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string? Initials { get; set; }
    public StudentAgeGroup? AgeGroup { get; set; }
    public Gender? Gender { get; set; }
    public Ethnicity? Ethnicity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<PersonEmail> Emails { get; } = [];
}
