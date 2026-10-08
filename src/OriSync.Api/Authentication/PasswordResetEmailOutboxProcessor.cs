using System.Data;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;

namespace OriSync.Api.Authentication;

public sealed partial class PasswordResetEmailOutboxProcessor(
    OriSyncDbContext dbContext,
    IPasswordResetCodeGenerator codeGenerator,
    IPasswordResetEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<PasswordResetEmailOutboxProcessor> logger)
{
    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // SKIP LOCKED allows another app instance to dispatch a different row without
        // sending the same email twice. The provider idempotency key covers a crash
        // after Resend accepts the message but before this transaction commits.
        var pendingRows = await dbContext.PasswordResetEmailOutbox
            .FromSqlInterpolated($"""
                SELECT *
                FROM password_reset_email_outbox
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
            .Reference(item => item.PasswordResetOtp)
            .Query()
            .Include(otp => otp.Account)
            .LoadAsync(cancellationToken);
        var otp = outbox.PasswordResetOtp;

        if (outbox.DiscardAfter <= now ||
            otp.ConsumedAt is not null ||
            !otp.Account.IsActive ||
            string.IsNullOrWhiteSpace(otp.CodeNonce))
        {
            Discard(outbox, otp, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (!codeGenerator.TryRegenerate(otp.CodeNonce, out var code))
        {
            LogInvalidCodeConfiguration(logger, outbox.Id);
            ScheduleRetry(outbox, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var sent = await emailSender.SendAsync(
            outbox.RecipientEmail,
            outbox.RecipientFirstName,
            code,
            $"mentor-password-reset/{outbox.Id}",
            cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        if (!sent)
        {
            ScheduleRetry(outbox, completedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await dbContext.PasswordResetOtps
            .Where(item =>
                item.AccountId == otp.AccountId &&
                item.Id != otp.Id &&
                item.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ConsumedAt, completedAt),
                cancellationToken);
        otp.SentAt = completedAt;
        otp.ExpiresAt = completedAt + PasswordResetConstants.OtpLifetime;
        otp.CodeNonce = null;
        outbox.SentAt = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void Discard(
        Domain.PasswordResetEmailOutbox outbox,
        Domain.PasswordResetOtp otp,
        DateTimeOffset now)
    {
        outbox.DiscardedAt = now;
        otp.ConsumedAt ??= now;
        otp.CodeNonce = null;
    }

    private static void ScheduleRetry(
        Domain.PasswordResetEmailOutbox outbox,
        DateTimeOffset now)
    {
        outbox.AttemptCount++;
        var exponent = Math.Min(outbox.AttemptCount - 1, 4);
        var delaySeconds = Math.Min(5 * (1 << exponent), 60);
        outbox.AvailableAt = now + TimeSpan.FromSeconds(delaySeconds);
    }

    [LoggerMessage(
        LogLevel.Error,
        "Password-reset outbox item {OutboxId} cannot regenerate its code because the code secret is invalid.")]
    private static partial void LogInvalidCodeConfiguration(ILogger logger, long outboxId);
}
