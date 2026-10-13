using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;

namespace OriSync.Api.StudentManagement;

public sealed class StudentTransferReconciler(OriSyncDbContext dbContext)
{
    public Task ReconcileAsync(
        long orientationId,
        DateOnly localDate,
        CancellationToken cancellationToken) =>
        dbContext.StudentEnrollments
            .Where(item => item.OrientationId == orientationId &&
                item.PendingGroupId != null &&
                item.PendingGroupEffectiveDate <= localDate)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentGroupId, item => item.PendingGroupId)
                    .SetProperty(item => item.PendingGroupId, (long?)null)
                    .SetProperty(item => item.PendingGroupEffectiveDate, (DateOnly?)null)
                    .SetProperty(item => item.RemovedAt, (DateTimeOffset?)null),
                cancellationToken);
}
