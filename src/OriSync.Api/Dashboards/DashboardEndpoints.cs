using System.Globalization;
using System.Security.Claims;
using OriSync.Api.Authentication;

namespace OriSync.Api.Dashboards;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/dashboard", async (
                DashboardService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetAdminAsync(cancellationToken)))
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<DashboardExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        endpoints.MapGet("/api/mentor/dashboard", async (
                ClaimsPrincipal principal,
                DashboardService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.GetMentorAsync(
                    GetAccountId(principal),
                    cancellationToken)))
            .RequireAuthorization(policy => policy.RequireRole("Mentor"))
            .AddEndpointFilter<DashboardExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    private static long GetAccountId(ClaimsPrincipal principal) =>
        long.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var accountId)
            ? accountId
            : throw new DashboardException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The account session is invalid.");
}
