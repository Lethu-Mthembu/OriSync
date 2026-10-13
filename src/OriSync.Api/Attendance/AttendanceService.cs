using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.StudentManagement;
using OriSync.Api.StudentQrCodes;

namespace OriSync.Api.Attendance;

public sealed class AttendanceService(
    OriSyncDbContext dbContext,
    TimeProvider timeProvider,
    StudentQrCodeTokenService tokenService,
    DailyAttendanceMaterializer materializer,
    StudentTransferReconciler transferReconciler)
{
    public async Task<AttendanceMutationResponse> ScanAsync(
        long mentorAccountId,
        ScanAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(mentorAccountId, cancellationToken);
        EnsureAttendanceOpen(context);
        await transferReconciler.ReconcileAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        await materializer.EnsureGroupAsync(
            context.Orientation.Id,
            context.Group.Id,
            context.LocalDate,
            cancellationToken);

        if (!tokenService.TryValidate(request.DecodedUrl, out var payload) || payload is null)
        {
            throw InvalidQr();
        }

        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .Include(item => item.CurrentGroup)
            .Include(item => item.PendingGroup)
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == context.LocalDate))
            .SingleOrDefaultAsync(
                item => item.OrientationId == context.Orientation.Id &&
                    item.Student.InstitutionNumber == payload.StudentNumber,
                cancellationToken)
            ?? throw InvalidQr();

        if (!MatchesCurrentStudent(enrollment, payload))
        {
            throw new AttendanceException(
                StatusCodes.Status409Conflict,
                "ExpiredQr",
                "Expired QR code.",
                "This QR code expired after the student's details changed. Download the replacement QR code.");
        }

        if (enrollment.CurrentGroupId != context.Group.Id)
        {
            var group = enrollment.CurrentGroup ?? enrollment.PendingGroup;
            var groupName = group?.Name ?? "UNASSIGNED";
            throw new AttendanceException(
                StatusCodes.Status409Conflict,
                "WrongGroup",
                "Wrong group.",
                $"{DisplayName(enrollment)} belongs to {groupName} Group. Attendance was not accepted.",
                new Dictionary<string, object?> { ["groupName"] = groupName });
        }

        var attendance = enrollment.AttendanceRecords.SingleOrDefault()
            ?? throw new InvalidOperationException("Today's attendance row was not materialized.");
        if (attendance.Status == AttendanceStatus.Present)
        {
            return Response("AlreadyPresent", enrollment, attendance.Status,
                $"{DisplayName(enrollment)} is already Present.");
        }

        attendance.Status = AttendanceStatus.Present;
        attendance.CaptureMethod = AttendanceCaptureMethod.Qr;
        attendance.MarkedByAccountId = mentorAccountId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Response("MarkedPresent", enrollment, attendance.Status,
            $"{DisplayName(enrollment)} was marked Present.");
    }

    public async Task<AttendanceMutationResponse> CorrectAsync(
        long mentorAccountId,
        long enrollmentId,
        CorrectAttendanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Status))
        {
            throw new AttendanceException(
                StatusCodes.Status400BadRequest,
                "InvalidStatus",
                "Invalid attendance status.",
                "Attendance status must be Present or Absent.");
        }

        var context = await GetContextAsync(mentorAccountId, cancellationToken);
        EnsureAttendanceOpen(context);
        await transferReconciler.ReconcileAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        await materializer.EnsureGroupAsync(
            context.Orientation.Id,
            context.Group.Id,
            context.LocalDate,
            cancellationToken);

        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.Student)
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == context.LocalDate))
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId &&
                    item.OrientationId == context.Orientation.Id &&
                    item.CurrentGroupId == context.Group.Id,
                cancellationToken)
            ?? throw new AttendanceException(
                StatusCodes.Status404NotFound,
                "StudentNotFound",
                "Student not found.",
                "The student is not available to this mentor group.");

        var attendance = enrollment.AttendanceRecords.SingleOrDefault()
            ?? throw new InvalidOperationException("Today's attendance row was not materialized.");
        attendance.Status = request.Status;
        attendance.CaptureMethod = AttendanceCaptureMethod.Manual;
        attendance.MarkedByAccountId = mentorAccountId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Response("Corrected", enrollment, attendance.Status,
            $"{DisplayName(enrollment)} was marked {attendance.Status}.");
    }

    private async Task<AttendanceContext> GetContextAsync(
        long mentorAccountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Include(item => item.CurrentGroup)
                .ThenInclude(group => group!.Orientation)
            .SingleOrDefaultAsync(
                item => item.Id == mentorAccountId &&
                    item.Role == AccountRole.Mentor &&
                    item.IsActive,
                cancellationToken)
            ?? throw new AttendanceException(
                StatusCodes.Status401Unauthorized,
                "MentorUnavailable",
                "Authentication required.",
                "The mentor account is unavailable.");
        var group = account.CurrentGroup;
        if (group is null || !group.IsActive || !group.Orientation.IsActive)
        {
            throw new AttendanceException(
                StatusCodes.Status409Conflict,
                "GroupUnavailable",
                "Attendance unavailable.",
                "Your account is not assigned to an active orientation group.");
        }

        var zone = FindTimeZone(group.Orientation.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        return new AttendanceContext(
            group,
            group.Orientation,
            DateOnly.FromDateTime(local.DateTime),
            TimeOnly.FromDateTime(local.DateTime));
    }

    private static void EnsureAttendanceOpen(AttendanceContext context)
    {
        if (!IsOrientationDay(context.Orientation, context.LocalDate) ||
            context.LocalTime < context.Orientation.AttendanceOpensAt ||
            context.LocalTime > context.Orientation.AttendanceClosesAt)
        {
            throw new AttendanceException(
                StatusCodes.Status409Conflict,
                "AttendanceClosed",
                "Attendance is closed.",
                $"Attendance can be marked only on orientation weekdays from " +
                $"{context.Orientation.AttendanceOpensAt:HH:mm} to " +
                $"{context.Orientation.AttendanceClosesAt:HH:mm}.");
        }
    }

    private static bool IsOrientationDay(Orientation orientation, DateOnly date) =>
        date >= orientation.StartDate &&
        date <= orientation.EndDate &&
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static TimeZoneInfo FindTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"Configured orientation timezone '{timeZoneId}' is unavailable.");
        }
    }

    private static bool MatchesCurrentStudent(
        StudentEnrollment enrollment,
        StudentQrCodePayload payload)
    {
        var studentEmail = enrollment.Student.Emails.SingleOrDefault(
            item => item.EmailType == EmailType.Student)?.Email;
        return enrollment.QrVersion == payload.Version &&
            string.Equals(enrollment.Student.InstitutionNumber, payload.StudentNumber, StringComparison.Ordinal) &&
            string.Equals(enrollment.Student.FirstName, payload.FullNames, StringComparison.Ordinal) &&
            string.Equals(enrollment.Student.Surname, payload.Surname, StringComparison.Ordinal) &&
            string.Equals(studentEmail, payload.StudentEmail, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(StudentEnrollment enrollment) =>
        $"{enrollment.Student.FirstName} {enrollment.Student.Surname}";

    private static AttendanceMutationResponse Response(
        string outcome,
        StudentEnrollment enrollment,
        AttendanceStatus status,
        string message) => new(
            outcome,
            enrollment.Id,
            enrollment.Student.InstitutionNumber!,
            enrollment.Student.FirstName,
            enrollment.Student.Surname,
            status,
            message);

    private static AttendanceException InvalidQr() => new(
        StatusCodes.Status400BadRequest,
        "InvalidQr",
        "Invalid QR code.",
        "This is not a valid OriSync student QR code.");

    private sealed record AttendanceContext(
        OrientationGroup Group,
        Orientation Orientation,
        DateOnly LocalDate,
        TimeOnly LocalTime);
}
