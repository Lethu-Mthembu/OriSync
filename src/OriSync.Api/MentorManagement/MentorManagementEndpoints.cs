using System.Globalization;
using System.Security.Claims;
using OriSync.Api.Authentication;

namespace OriSync.Api.MentorManagement;

public static class MentorManagementEndpoints
{
    public static IEndpointRouteBuilder MapMentorManagementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<MentorManagementExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        admin.MapGet("/mentors", async (
            string? search,
            long? groupId,
            string? status,
            int? page,
            int? pageSize,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetDirectoryAsync(
                search,
                groupId,
                status,
                page ?? 1,
                pageSize ?? 20,
                cancellationToken)));

        admin.MapPost("/mentor-invitations", async (
            CreateMentorInvitationRequest request,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateInvitationAsync(
                request,
                GetAccountId(principal),
                cancellationToken);
            return Results.Created($"/api/admin/mentor-invitations/{created.Id}", created);
        });

        admin.MapPut("/mentor-invitations/{invitationId:long}/group", async (
            long invitationId,
            ChangeMentorInvitationGroupRequest request,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ChangeInvitationGroupAsync(
                invitationId,
                request,
                GetAccountId(principal),
                cancellationToken)));

        admin.MapPost("/mentor-invitations/{invitationId:long}/resend", async (
            long invitationId,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ResendInvitationAsync(
                invitationId,
                GetAccountId(principal),
                cancellationToken)));

        admin.MapPost("/mentor-invitations/{invitationId:long}/cancel", async (
            long invitationId,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.CancelInvitationAsync(
                invitationId,
                GetAccountId(principal),
                cancellationToken);
            return Results.NoContent();
        });

        admin.MapPut("/mentors/{accountId:long}", async (
            long accountId,
            UpdateMentorRequest request,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateMentorAsync(
                accountId,
                request,
                GetAccountId(principal),
                cancellationToken)));

        admin.MapPost("/mentors/{accountId:long}/disable", async (
            long accountId,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.DisableMentorAsync(
                accountId,
                GetAccountId(principal),
                cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/mentors/{accountId:long}/reactivate", async (
            long accountId,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.ReactivateMentorAsync(
                accountId,
                GetAccountId(principal),
                cancellationToken);
            return Results.NoContent();
        });

        admin.MapDelete("/mentors/{accountId:long}", async (
            long accountId,
            ClaimsPrincipal principal,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteMentorAsync(
                accountId,
                GetAccountId(principal),
                cancellationToken);
            return Results.NoContent();
        });

        var activation = endpoints.MapGroup("/api/auth/mentor-activation")
            .AllowAnonymous()
            .AddEndpointFilter<MentorManagementExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        activation.MapPost("/validate", async (
            ValidateMentorInvitationRequest request,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ValidateInvitationAsync(request, cancellationToken)));

        activation.MapPost("/accept", async (
            AcceptMentorInvitationRequest request,
            MentorManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.AcceptInvitationAsync(request, cancellationToken);
            return Results.NoContent();
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
            : throw new MentorManagementException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The administrator session is invalid.");
}
