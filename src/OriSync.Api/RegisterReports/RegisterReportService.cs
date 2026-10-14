using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Attendance;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.StudentManagement;

namespace OriSync.Api.RegisterReports;

public sealed class RegisterReportService(
    OriSyncDbContext dbContext,
    TimeProvider timeProvider,
    DailyAttendanceMaterializer attendanceMaterializer,
    StudentTransferReconciler transferReconciler,
    RegisterPdfRenderer pdfRenderer)
{
    public async Task<RegisterSummaryResponse> GetMentorSummaryAsync(
        long accountId,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await PrepareCurrentDateAsync(
            context.Orientation,
            context.LocalDate,
            context.Group.Id,
            cancellationToken);
        var rowCount = await FilteredAttendance(
                context.Orientation.Id,
                context.LocalDate,
                context.Group.Id,
                filter)
            .CountAsync(cancellationToken);
        return new RegisterSummaryResponse(rowCount, rowCount == 0 ? 0 : 1, filter);
    }

    public async Task<RegisterDocument> CreateMentorPdfAsync(
        long accountId,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await PrepareCurrentDateAsync(
            context.Orientation,
            context.LocalDate,
            context.Group.Id,
            cancellationToken);
        return await CreatePdfAsync(
            context.Orientation,
            context.Group,
            context.LocalDate,
            filter,
            context.LocalTime,
            cancellationToken);
    }

    public async Task<RegisterSummaryResponse> GetAdminSummaryAsync(
        long orientationId,
        DateOnly attendanceDate,
        long? groupId,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        var context = await GetAdminContextAsync(
            orientationId,
            attendanceDate,
            groupId,
            cancellationToken);
        var query = FilteredAttendance(
            context.Orientation.Id,
            attendanceDate,
            groupId,
            filter);
        var rowCount = await query.CountAsync(cancellationToken);
        var groupCount = rowCount == 0
            ? 0
            : await query.Select(item => item.GroupId).Distinct().CountAsync(cancellationToken);
        return new RegisterSummaryResponse(rowCount, groupCount, filter);
    }

    public async Task<IReadOnlyList<RegisterGroupSummaryResponse>> GetAdminGroupSummariesAsync(
        long orientationId,
        DateOnly attendanceDate,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        await GetAdminContextAsync(
            orientationId,
            attendanceDate,
            null,
            cancellationToken);

        var counts = await dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(item =>
                item.OrientationId == orientationId &&
                item.AttendanceDate == attendanceDate)
            .GroupBy(item => item.GroupId)
            .Select(group => new
            {
                GroupId = group.Key,
                Total = group.Count(),
                Present = group.Count(item => item.Status == AttendanceStatus.Present),
                Absent = group.Count(item => item.Status == AttendanceStatus.Absent)
            })
            .ToDictionaryAsync(item => item.GroupId, cancellationToken);

        var groups = await dbContext.Groups
            .AsNoTracking()
            .Where(item => item.OrientationId == orientationId)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name, item.BadgeColor })
            .ToArrayAsync(cancellationToken);

        return groups.Select(group =>
        {
            counts.TryGetValue(group.Id, out var groupCounts);
            var total = groupCounts?.Total ?? 0;
            var present = groupCounts?.Present ?? 0;
            var absent = groupCounts?.Absent ?? 0;
            var matching = filter switch
            {
                RegisterFilter.Present => present,
                RegisterFilter.Absent => absent,
                _ => total
            };
            return new RegisterGroupSummaryResponse(
                group.Id,
                group.Name,
                group.BadgeColor,
                total,
                present,
                absent,
                matching);
        }).ToArray();
    }

    public async Task<RegisterDocument> CreateAdminPdfAsync(
        long orientationId,
        DateOnly attendanceDate,
        long groupId,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        var context = await GetAdminContextAsync(
            orientationId,
            attendanceDate,
            groupId,
            cancellationToken);
        return await CreatePdfAsync(
            context.Orientation,
            context.Group!,
            attendanceDate,
            filter,
            context.LocalTime,
            cancellationToken);
    }

    public async Task<RegisterDocument> CreateAdminZipAsync(
        long orientationId,
        DateOnly attendanceDate,
        RegisterFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureValidFilter(filter);
        var context = await GetAdminContextAsync(
            orientationId,
            attendanceDate,
            null,
            cancellationToken);
        var groupIds = await FilteredAttendance(
                orientationId,
                attendanceDate,
                null,
                filter)
            .Select(item => item.GroupId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (groupIds.Length == 0)
        {
            throw EmptyRegister();
        }

        var groups = await dbContext.Groups
            .AsNoTracking()
            .Where(item => item.OrientationId == orientationId && groupIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var group in groups)
            {
                var pdf = await CreatePdfAsync(
                    context.Orientation,
                    group,
                    attendanceDate,
                    filter,
                    context.LocalTime,
                    cancellationToken);
                var entry = archive.CreateEntry(pdf.FileName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdf.Content, cancellationToken);
            }
        }

        var fileName = $"OriSync-{attendanceDate:yyyy-MM-dd}-" +
            $"{context.LocalTime:HH'h'mm}-{filter}.zip";
        return new RegisterDocument(stream.ToArray(), "application/zip", fileName);
    }

    private async Task<AdminRegisterContext> GetAdminContextAsync(
        long orientationId,
        DateOnly attendanceDate,
        long? groupId,
        CancellationToken cancellationToken)
    {
        var orientation = await dbContext.Orientations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == orientationId, cancellationToken)
            ?? throw new RegisterReportException(
                StatusCodes.Status404NotFound,
                "Orientation not found.",
                "The selected orientation does not exist.");
        EnsureOperatingDate(orientation, attendanceDate);
        var local = GetLocalDateTime(orientation);
        if (attendanceDate > local.Date)
        {
            throw new RegisterReportException(
                StatusCodes.Status400BadRequest,
                "Register unavailable.",
                "A future attendance register cannot be downloaded.");
        }

        OrientationGroup? group = null;
        if (groupId is not null)
        {
            group = await dbContext.Groups
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.Id == groupId.Value && item.OrientationId == orientationId,
                    cancellationToken)
                ?? throw new RegisterReportException(
                    StatusCodes.Status404NotFound,
                    "Group not found.",
                    "The selected group does not belong to this orientation.");
        }

        await PrepareCurrentDateAsync(
            orientation,
            attendanceDate,
            groupId,
            cancellationToken);
        return new AdminRegisterContext(orientation, group, local.Time);
    }

    private async Task<MentorRegisterContext> GetMentorContextAsync(
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
            ?? throw new RegisterReportException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The mentor account is unavailable.");
        var group = account.CurrentGroup;
        if (group is null || !group.IsActive || !group.Orientation.IsActive)
        {
            throw new RegisterReportException(
                StatusCodes.Status409Conflict,
                "Register unavailable.",
                "Your account is not assigned to an active orientation group.");
        }

        var local = GetLocalDateTime(group.Orientation);
        return new MentorRegisterContext(group.Orientation, group, local.Date, local.Time);
    }

    private async Task PrepareCurrentDateAsync(
        Orientation orientation,
        DateOnly attendanceDate,
        long? groupId,
        CancellationToken cancellationToken)
    {
        var localDate = GetLocalDateTime(orientation).Date;
        if (attendanceDate != localDate || !IsOperatingDate(orientation, attendanceDate))
        {
            return;
        }

        await transferReconciler.ReconcileAsync(orientation.Id, localDate, cancellationToken);
        if (groupId is null)
        {
            await attendanceMaterializer.EnsureOrientationAsync(
                orientation.Id,
                localDate,
                cancellationToken);
        }
        else
        {
            await attendanceMaterializer.EnsureGroupAsync(
                orientation.Id,
                groupId.Value,
                localDate,
                cancellationToken);
        }
    }

    private async Task<RegisterDocument> CreatePdfAsync(
        Orientation orientation,
        OrientationGroup group,
        DateOnly attendanceDate,
        RegisterFilter filter,
        TimeOnly localTime,
        CancellationToken cancellationToken)
    {
        var rows = await FilteredAttendance(
                orientation.Id,
                attendanceDate,
                group.Id,
                filter)
            .OrderBy(item => item.StudentEnrollment.Student.InstitutionNumber)
            .ThenBy(item => item.Id)
            .Select(item => new RegisterRow(
                item.StudentEnrollment.Student.Initials! + " " +
                    item.StudentEnrollment.Student.Surname,
                item.StudentEnrollment.Student.InstitutionNumber!,
                item.StudentEnrollment.Student.AgeGroup!.Value,
                item.StudentEnrollment.Student.Gender!.Value,
                item.StudentEnrollment.Student.Ethnicity!.Value,
                item.Status))
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0)
        {
            throw EmptyRegister();
        }

        var content = pdfRenderer.Render(group.Name, attendanceDate, rows);
        var fileName = $"{FileGroupName(group.Name)}-{attendanceDate:yyyy-MM-dd}-" +
            $"{localTime:HH'h'mm}-{filter}.pdf";
        return new RegisterDocument(content, "application/pdf", fileName);
    }

    private IQueryable<AttendanceRecord> FilteredAttendance(
        long orientationId,
        DateOnly attendanceDate,
        long? groupId,
        RegisterFilter filter)
    {
        var query = dbContext.AttendanceRecords
            .AsNoTracking()
            .Where(item =>
                item.OrientationId == orientationId &&
                item.AttendanceDate == attendanceDate);
        if (groupId is not null)
        {
            query = query.Where(item => item.GroupId == groupId.Value);
        }

        return filter switch
        {
            RegisterFilter.Present => query.Where(item => item.Status == AttendanceStatus.Present),
            RegisterFilter.Absent => query.Where(item => item.Status == AttendanceStatus.Absent),
            _ => query
        };
    }

    private static void EnsureValidFilter(RegisterFilter filter)
    {
        if (!Enum.IsDefined(filter))
        {
            throw new RegisterReportException(
                StatusCodes.Status400BadRequest,
                "Invalid register filter.",
                "Attendance status must be All, Present or Absent.");
        }
    }

    private static void EnsureOperatingDate(Orientation orientation, DateOnly date)
    {
        if (!IsOperatingDate(orientation, date))
        {
            throw new RegisterReportException(
                StatusCodes.Status400BadRequest,
                "Invalid attendance date.",
                "The selected date is not an orientation weekday.");
        }
    }

    private static bool IsOperatingDate(Orientation orientation, DateOnly date) =>
        date >= orientation.StartDate &&
        date <= orientation.EndDate &&
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private (DateOnly Date, TimeOnly Time) GetLocalDateTime(Orientation orientation)
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
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    private static string FileGroupName(string groupName)
    {
        var titled = CultureInfo.GetCultureInfo("en-ZA").TextInfo.ToTitleCase(
            groupName.ToLower(CultureInfo.GetCultureInfo("en-ZA")));
        var normalized = new string(titled
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    private static RegisterReportException EmptyRegister() => new(
        StatusCodes.Status409Conflict,
        "Register is empty.",
        "The selected filter contains no students, so no file was generated.");

    private sealed record MentorRegisterContext(
        Orientation Orientation,
        OrientationGroup Group,
        DateOnly LocalDate,
        TimeOnly LocalTime);

    private sealed record AdminRegisterContext(
        Orientation Orientation,
        OrientationGroup? Group,
        TimeOnly LocalTime);
}
