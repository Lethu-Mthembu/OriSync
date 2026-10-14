using System.Globalization;
using System.Security.Claims;
using OriSync.Api.Authentication;
using OriSync.Api.Domain;
using OriSync.Api.StudentQrCodes;

namespace OriSync.Api.StudentManagement;

public static class StudentManagementEndpoints
{
    public static IEndpointRouteBuilder MapStudentManagementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var mentor = endpoints.MapGroup("/api/mentor/students")
            .RequireAuthorization(policy => policy.RequireRole("Mentor"))
            .AddEndpointFilter<StudentManagementExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        mentor.MapGet("", async (
            string? search,
            AttendanceStatus? attendanceStatus,
            int? page,
            int? pageSize,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMentorDirectoryAsync(
                GetAccountId(principal),
                search,
                attendanceStatus,
                page ?? 1,
                pageSize ?? 10,
                cancellationToken)));

        mentor.MapGet("/lookup/{studentNumber}", async (
            string studentNumber,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.LookupAsync(
                GetAccountId(principal),
                studentNumber,
                cancellationToken)));

        mentor.MapGet("/{enrollmentId:long}", async (
            long enrollmentId,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetProfileAsync(
                GetAccountId(principal),
                enrollmentId,
                cancellationToken)));

        mentor.MapGet("/{enrollmentId:long}/qr", async (
            long enrollmentId,
            HttpContext context,
            ClaimsPrincipal principal,
            StudentQrCodeService service,
            CancellationToken cancellationToken) =>
        {
            var document = await service.CreateAsync(
                GetAccountId(principal),
                enrollmentId,
                cancellationToken);
            PreventQrCaching(context.Response);
            context.Response.Headers.ContentDisposition =
                $"inline; filename=\"{document.FileName}\"";
            return Results.File(document.Content, "image/png");
        });

        mentor.MapGet("/{enrollmentId:long}/qr/download", async (
            long enrollmentId,
            HttpContext context,
            ClaimsPrincipal principal,
            StudentQrCodeService service,
            CancellationToken cancellationToken) =>
        {
            var document = await service.CreateAsync(
                GetAccountId(principal),
                enrollmentId,
                cancellationToken);
            PreventQrCaching(context.Response);
            return Results.File(document.Content, "image/png", document.FileName);
        });

        mentor.MapPost("/{enrollmentId:long}/qr/email", async (
            long enrollmentId,
            ClaimsPrincipal principal,
            StudentQrCodeService service,
            CancellationToken cancellationToken) =>
            Results.Accepted(
                value: await service.QueueEmailAsync(
                    GetAccountId(principal),
                    enrollmentId,
                    cancellationToken)));

        mentor.MapPost("", async (
            SaveStudentRequest request,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateAsync(
                GetAccountId(principal),
                request,
                cancellationToken);
            return Results.Created($"/api/mentor/students/{created.Student.EnrollmentId}", created);
        });

        mentor.MapPost("/onboard", async (
            OnboardExistingStudentRequest request,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.OnboardExistingAsync(
                GetAccountId(principal),
                request,
                cancellationToken)));

        mentor.MapPut("/{enrollmentId:long}", async (
            long enrollmentId,
            SaveStudentRequest request,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(
                GetAccountId(principal),
                enrollmentId,
                request,
                cancellationToken)));

        mentor.MapPost("/{enrollmentId:long}/remove", async (
            long enrollmentId,
            ClaimsPrincipal principal,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.RemoveAsync(
                GetAccountId(principal),
                enrollmentId,
                cancellationToken);
            return Results.NoContent();
        });

        var admin = endpoints.MapGroup("/api/admin/students")
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<StudentManagementExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        admin.MapGet("", async (
            string? search,
            long? groupId,
            bool? unassigned,
            AttendanceStatus? attendanceStatus,
            int? page,
            int? pageSize,
            StudentManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAdminDirectoryAsync(
                search,
                groupId,
                unassigned ?? false,
                attendanceStatus,
                page ?? 1,
                pageSize ?? 10,
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
            : throw new StudentManagementException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The account session is invalid.");

    private static void PreventQrCaching(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
