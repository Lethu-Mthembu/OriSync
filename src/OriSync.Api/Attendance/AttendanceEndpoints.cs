using System.Globalization;
using System.Security.Claims;
using OriSync.Api.Authentication;

namespace OriSync.Api.Attendance;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var attendance = endpoints.MapGroup("/api/mentor/attendance")
            .RequireAuthorization(policy => policy.RequireRole("Mentor"))
            .AddEndpointFilter<AttendanceExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        attendance.MapPost("/scan", async (
            ScanAttendanceRequest request,
            ClaimsPrincipal principal,
            AttendanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ScanAsync(
                GetAccountId(principal),
                request,
                cancellationToken)));

        attendance.MapPut("/{enrollmentId:long}", async (
            long enrollmentId,
            CorrectAttendanceRequest request,
            ClaimsPrincipal principal,
            AttendanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.CorrectAsync(
                GetAccountId(principal),
                enrollmentId,
                request,
                cancellationToken)));

        return endpoints;
    }

    private static long GetAccountId(ClaimsPrincipal principal) =>
        long.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var accountId)
            ? accountId
            : throw new AttendanceException(
                StatusCodes.Status401Unauthorized,
                "InvalidSession",
                "Authentication required.",
                "The account session is invalid.");
}
