using Microsoft.EntityFrameworkCore;
using OriSync.Api.Attendance;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.StudentManagement;

namespace OriSync.Api.Dashboards;

public sealed class DashboardService(
    OriSyncDbContext dbContext,
    TimeProvider timeProvider,
    DailyAttendanceMaterializer attendanceMaterializer,
    StudentTransferReconciler transferReconciler)
{
    public async Task<AdminDashboardResponse> GetAdminAsync(CancellationToken cancellationToken)
    {
        var orientation = await dbContext.Orientations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IsActive, cancellationToken);
        if (orientation is null)
        {
            return new AdminDashboardResponse(
                false,
                null,
                null,
                new DashboardMetrics(0, 0, 0, 0),
                []);
        }

        var localDate = GetLocalDate(orientation);
        await PrepareTodayAsync(orientation, localDate, cancellationToken);

        var totalStudents = await dbContext.StudentEnrollments
            .CountAsync(item => item.OrientationId == orientation.Id, cancellationToken);
        var attendance = await CountAttendanceAsync(
            orientation.Id,
            null,
            localDate,
            cancellationToken);

        var groups = await dbContext.Groups
            .AsNoTracking()
            .Where(group => group.OrientationId == orientation.Id && group.IsActive)
            .Select(group => new
            {
                group.Id,
                group.Name,
                group.BadgeColor,
                StudentCount = dbContext.StudentEnrollments.Count(enrollment =>
                    enrollment.OrientationId == orientation.Id &&
                    enrollment.CurrentGroupId == group.Id)
            })
            .OrderByDescending(group => group.StudentCount)
            .ThenBy(group => group.Name)
            .ToListAsync(cancellationToken);

        return new AdminDashboardResponse(
            true,
            orientation.Name,
            localDate,
            Metrics(totalStudents, attendance.Present, attendance.Absent),
            groups.Select((group, index) => new GroupStudentCountResponse(
                group.Id,
                group.Name,
                group.BadgeColor,
                group.StudentCount,
                index + 1)).ToArray());
    }

    public async Task<MentorDashboardResponse> GetMentorAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Include(item => item.CurrentGroup)
                .ThenInclude(group => group!.Orientation)
            .SingleOrDefaultAsync(item =>
                item.Id == accountId &&
                item.Role == AccountRole.Mentor &&
                item.IsActive,
                cancellationToken)
            ?? throw new DashboardException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The mentor account is unavailable.");

        var group = account.CurrentGroup;
        if (group is null || !group.IsActive || !group.Orientation.IsActive)
        {
            throw new DashboardException(
                StatusCodes.Status409Conflict,
                "Dashboard unavailable.",
                "Your account is not assigned to an active orientation group.");
        }

        var localDate = GetLocalDate(group.Orientation);
        await PrepareTodayAsync(group.Orientation, localDate, cancellationToken);
        var attendance = await CountAttendanceAsync(
            group.OrientationId,
            group.Id,
            localDate,
            cancellationToken);
        var registerTotal = attendance.Present + attendance.Absent;

        return new MentorDashboardResponse(
            group.Orientation.Name,
            localDate,
            group.Id,
            group.Name,
            group.BadgeColor,
            Metrics(registerTotal, attendance.Present, attendance.Absent));
    }

    private async Task PrepareTodayAsync(
        Orientation orientation,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        await transferReconciler.ReconcileAsync(orientation.Id, localDate, cancellationToken);
        if (IsOrientationDay(orientation, localDate))
        {
            await attendanceMaterializer.EnsureOrientationAsync(
                orientation.Id,
                localDate,
                cancellationToken);
        }
    }

    private async Task<(int Present, int Absent)> CountAttendanceAsync(
        long orientationId,
        long? groupId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(item =>
                item.OrientationId == orientationId &&
                item.AttendanceDate == localDate);
        if (groupId is not null)
        {
            query = query.Where(item => item.GroupId == groupId.Value);
        }

        var counts = await query
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return (
            counts.SingleOrDefault(item => item.Status == AttendanceStatus.Present)?.Count ?? 0,
            counts.SingleOrDefault(item => item.Status == AttendanceStatus.Absent)?.Count ?? 0);
    }

    private static DashboardMetrics Metrics(int totalStudents, int present, int absent)
    {
        var attendanceTotal = present + absent;
        var rate = attendanceTotal == 0
            ? 0
            : Math.Round(present * 100m / attendanceTotal, 1, MidpointRounding.AwayFromZero);
        return new DashboardMetrics(totalStudents, present, absent, rate);
    }

    private DateOnly GetLocalDate(Orientation orientation)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(orientation.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"Configured orientation timezone '{orientation.TimeZoneId}' is unavailable.");
        }

        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static bool IsOrientationDay(Orientation orientation, DateOnly date) =>
        date >= orientation.StartDate &&
        date <= orientation.EndDate &&
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
}
