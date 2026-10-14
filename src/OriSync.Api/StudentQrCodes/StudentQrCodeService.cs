using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.StudentManagement;

namespace OriSync.Api.StudentQrCodes;

public sealed class StudentQrCodeService(
    OriSyncDbContext dbContext,
    StudentQrCodeTokenService tokenService,
    StudentQrCodeImageRenderer imageRenderer,
    TimeProvider timeProvider)
{
    public async Task<StudentQrCodeDocument> CreateAsync(
        long mentorAccountId,
        long enrollmentId,
        CancellationToken cancellationToken)
    {
        var mentorGroupId = await dbContext.Accounts
            .AsNoTracking()
            .Where(account =>
                account.Id == mentorAccountId &&
                account.Role == AccountRole.Mentor &&
                account.IsActive &&
                account.CurrentGroup != null &&
                account.CurrentGroup.IsActive &&
                account.CurrentGroup.Orientation.IsActive)
            .Select(account => account.CurrentGroupId)
            .SingleOrDefaultAsync(cancellationToken);
        if (mentorGroupId is null)
        {
            throw NotFound();
        }

        var enrollment = await dbContext.StudentEnrollments
            .AsNoTracking()
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId && item.CurrentGroupId == mentorGroupId,
                cancellationToken)
            ?? throw NotFound();

        return CreateForEnrollment(enrollment);
    }

    public async Task<StudentQrEmailQueuedResponse> QueueEmailAsync(
        long mentorAccountId,
        long enrollmentId,
        CancellationToken cancellationToken)
    {
        var mentorGroupId = await dbContext.Accounts
            .AsNoTracking()
            .Where(account =>
                account.Id == mentorAccountId &&
                account.Role == AccountRole.Mentor &&
                account.IsActive &&
                account.CurrentGroup != null &&
                account.CurrentGroup.IsActive &&
                account.CurrentGroup.Orientation.IsActive)
            .Select(account => account.CurrentGroupId)
            .SingleOrDefaultAsync(cancellationToken);
        if (mentorGroupId is null)
        {
            throw NotFound();
        }

        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId && item.CurrentGroupId == mentorGroupId,
                cancellationToken)
            ?? throw NotFound();
        var personalEmail = enrollment.Student.Emails.SingleOrDefault(
            email => email.EmailType == EmailType.Personal)?.Email;
        if (string.IsNullOrWhiteSpace(personalEmail))
        {
            throw new StudentManagementException(
                StatusCodes.Status409Conflict,
                "Personal email required.",
                "Add the student's personal email before sending the QR code.");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.StudentQrEmailOutbox.Add(new StudentQrEmailOutbox
        {
            StudentEnrollmentId = enrollment.Id,
            QrVersion = enrollment.QrVersion,
            RecipientEmail = personalEmail,
            CreatedAt = now,
            AvailableAt = now,
            DiscardAfter = now + StudentQrEmailConstants.DeliveryLifetime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new StudentQrEmailQueuedResponse(
            personalEmail,
            $"The QR code was queued for delivery to {personalEmail}.");
    }

    internal StudentQrCodeDocument CreateForEnrollment(StudentEnrollment enrollment)
    {
        var studentEmail = enrollment.Student.Emails.SingleOrDefault(
            email => email.EmailType == EmailType.Student)?.Email;
        if (string.IsNullOrWhiteSpace(enrollment.Student.InstitutionNumber) ||
            string.IsNullOrWhiteSpace(studentEmail))
        {
            throw new InvalidOperationException(
                "A student QR code cannot be generated from incomplete student data.");
        }

        var payload = new StudentQrCodePayload(
            enrollment.Student.InstitutionNumber,
            enrollment.Student.FirstName,
            enrollment.Student.Surname,
            studentEmail,
            enrollment.QrVersion);
        var token = tokenService.Create(payload);
        var displayName = $"{payload.FullNames} {payload.Surname}";
        var image = imageRenderer.Render(token.Url, displayName);
        return new StudentQrCodeDocument(
            image,
            $"{payload.StudentNumber}-qr.png",
            token.Url);
    }

    private static StudentManagementException NotFound() => new(
        StatusCodes.Status404NotFound,
        "Student QR code not found.",
        "The student is not available to this mentor group.");
}
