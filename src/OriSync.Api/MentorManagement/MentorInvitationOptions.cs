namespace OriSync.Api.MentorManagement;

public sealed class MentorInvitationOptions
{
    public const string SectionName = "MentorInvitations";

    public bool DispatcherEnabled { get; set; } = true;
    public string? PublicBaseUrl { get; set; }
}
