using System.Globalization;
using System.Security.Claims;
using OriSync.Api.Authentication;

namespace OriSync.Api.RegisterReports;

public static class RegisterReportEndpoints
{
    public static IEndpointRouteBuilder MapRegisterReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var mentor = endpoints.MapGroup("/api/mentor/registers")
            .RequireAuthorization(policy => policy.RequireRole("Mentor"))
            .AddEndpointFilter<RegisterReportExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        mentor.MapGet("/today/summary", async (
            RegisterFilter? attendanceStatus,
            ClaimsPrincipal principal,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMentorSummaryAsync(
                GetAccountId(principal),
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken)));

        mentor.MapGet("/today", async (
            RegisterFilter? attendanceStatus,
            HttpContext context,
            ClaimsPrincipal principal,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
        {
            var document = await service.CreateMentorPdfAsync(
                GetAccountId(principal),
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken);
            PreventCaching(context.Response);
            return Results.File(document.Content, document.ContentType, document.FileName);
        });

        var admin = endpoints.MapGroup("/api/admin/registers")
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<RegisterReportExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        admin.MapGet("/summary", async (
            long orientationId,
            DateOnly date,
            long? groupId,
            RegisterFilter? attendanceStatus,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAdminSummaryAsync(
                orientationId,
                date,
                groupId,
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken)));

        admin.MapGet("/groups", async (
            long orientationId,
            DateOnly date,
            RegisterFilter? attendanceStatus,
            HttpContext context,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
        {
            var summaries = await service.GetAdminGroupSummariesAsync(
                orientationId,
                date,
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken);
            PreventCaching(context.Response);
            return Results.Ok(summaries);
        });

        admin.MapGet("/pdf", async (
            long orientationId,
            DateOnly date,
            long groupId,
            RegisterFilter? attendanceStatus,
            HttpContext context,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
        {
            var document = await service.CreateAdminPdfAsync(
                orientationId,
                date,
                groupId,
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken);
            PreventCaching(context.Response);
            return Results.File(document.Content, document.ContentType, document.FileName);
        });

        admin.MapGet("/zip", async (
            long orientationId,
            DateOnly date,
            RegisterFilter? attendanceStatus,
            HttpContext context,
            RegisterReportService service,
            CancellationToken cancellationToken) =>
        {
            var document = await service.CreateAdminZipAsync(
                orientationId,
                date,
                attendanceStatus ?? RegisterFilter.All,
                cancellationToken);
            PreventCaching(context.Response);
            return Results.File(document.Content, document.ContentType, document.FileName);
        });

        return endpoints;
    }

    private static long GetAccountId(ClaimsPrincipal principal) =>
        long.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var accountId)
            ? accountId
            : throw new RegisterReportException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The account session is invalid.");

    private static void PreventCaching(HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
    }
}
