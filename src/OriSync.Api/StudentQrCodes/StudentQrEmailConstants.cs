namespace OriSync.Api.StudentQrCodes;

public static class StudentQrEmailConstants
{
    public static readonly TimeSpan DeliveryLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan DispatcherPollInterval = TimeSpan.FromSeconds(2);
}
