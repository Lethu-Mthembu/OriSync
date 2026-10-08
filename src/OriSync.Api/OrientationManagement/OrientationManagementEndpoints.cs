using OriSync.Api.Authentication;

namespace OriSync.Api.OrientationManagement;

public static class OrientationManagementEndpoints
{
    public static IEndpointRouteBuilder MapOrientationManagementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var orientations = endpoints.MapGroup("/api/admin/orientations")
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .AddEndpointFilter<OrientationManagementExceptionFilter>()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        orientations.MapGet("", async (
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAllAsync(cancellationToken)));

        orientations.MapPost("", async (
            SaveOrientationRequest request,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/admin/orientations/{created.Id}", created);
        });

        orientations.MapPut("/{orientationId:long}", async (
            long orientationId,
            SaveOrientationRequest request,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(orientationId, request, cancellationToken)));

        orientations.MapPost("/{orientationId:long}/activate", async (
            long orientationId,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ActivateAsync(orientationId, cancellationToken)));

        orientations.MapPost("/{orientationId:long}/deactivate", async (
            long orientationId,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.DeactivateAsync(orientationId, cancellationToken)));

        orientations.MapPost("/{orientationId:long}/groups", async (
            long orientationId,
            SaveOrientationGroupRequest request,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
        {
            var updated = await service.CreateGroupAsync(
                orientationId,
                request,
                cancellationToken);
            return Results.Created($"/api/admin/orientations/{orientationId}/groups", updated);
        });

        orientations.MapPut("/{orientationId:long}/groups/{groupId:long}", async (
            long orientationId,
            long groupId,
            SaveOrientationGroupRequest request,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateGroupAsync(
                orientationId,
                groupId,
                request,
                cancellationToken)));

        orientations.MapPost("/{orientationId:long}/groups/{groupId:long}/activate", async (
            long orientationId,
            long groupId,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ActivateGroupAsync(
                orientationId,
                groupId,
                cancellationToken)));

        orientations.MapPost("/{orientationId:long}/groups/{groupId:long}/deactivate", async (
            long orientationId,
            long groupId,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.DeactivateGroupAsync(
                orientationId,
                groupId,
                cancellationToken)));

        orientations.MapDelete("/{orientationId:long}/groups/{groupId:long}", async (
            long orientationId,
            long groupId,
            OrientationManagementService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteGroupAsync(orientationId, groupId, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }
}
