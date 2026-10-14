using System.Data;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.StudentQrCodes;

public sealed class StudentQrEmailOutboxProcessor(
    OriSyncDbContext dbContext,
    StudentQrCodeService qrCodeService,
    IStudentQrEmailSender emailSender,
    TimeProvider timeProvider)
{
    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var pendingRows = await dbContext.StudentQrEmailOutbox
            .FromSqlInterpolated($"""
                SELECT *
                FROM student_qr_email_outbox
                WHERE sent_at IS NULL
                  AND discarded_at IS NULL
                  AND available_at <= {now}
                ORDER BY id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);
        var outbox = pendingRows.SingleOrDefault();
        if (outbox is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await dbContext.Entry(outbox)
            .Reference(item => item.StudentEnrollment)
            .Query()
            .Include(enrollment => enrollment.Student)
                .ThenInclude(student => student.Emails)
            .LoadAsync(cancellationToken);

        var enrollment = outbox.StudentEnrollment;
        var currentPersonalEmail = enrollment.Student.Emails.SingleOrDefault(
            email => email.EmailType == EmailType.Personal)?.Email;
        if (outbox.DiscardAfter <= now ||
            outbox.AttemptCount >= 20 ||
            enrollment.CurrentGroupId is null ||
            enrollment.QrVersion != outbox.QrVersion ||
            !string.Equals(currentPersonalEmail, outbox.RecipientEmail, StringComparison.OrdinalIgnoreCase))
        {
            outbox.DiscardedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var document = qrCodeService.CreateForEnrollment(enrollment);
        var sent = await emailSender.SendAsync(
            outbox.RecipientEmail,
            $"{enrollment.Student.FirstName} {enrollment.Student.Surname}",
            enrollment.Student.InstitutionNumber!,
            document,
            $"student-qr/{outbox.Id}",
            cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        if (!sent)
        {
            ScheduleRetry(outbox, completedAt);
        }
        else
        {
            outbox.SentAt = completedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void ScheduleRetry(StudentQrEmailOutbox outbox, DateTimeOffset now)
    {
        outbox.AttemptCount++;
        var exponent = Math.Min(outbox.AttemptCount - 1, 4);
        var delaySeconds = Math.Min(5 * (1 << exponent), 60);
        outbox.AvailableAt = now + TimeSpan.FromSeconds(delaySeconds);
    }
}
