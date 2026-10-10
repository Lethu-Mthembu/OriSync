using System.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.MentorManagement;

public sealed partial class MentorInvitationEmailProcessor(
    OriSyncDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IMentorInvitationEmailSender emailSender,
    IOptions<MentorInvitationOptions> options,
    TimeProvider timeProvider,
    ILogger<MentorInvitationEmailProcessor> logger)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(14);

    public async Task<bool> ProcessOneAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var pendingRows = await dbContext.MentorInvitations
            .FromSqlInterpolated($"""
                SELECT *
                FROM mentor_invitations
                WHERE sent_at IS NULL
                  AND accepted_at IS NULL
                  AND cancelled_at IS NULL
                  AND delivery_failed_at IS NULL
                  AND available_at <= {now}
                ORDER BY id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .ToListAsync(cancellationToken);
        var invitation = pendingRows.SingleOrDefault();
        if (invitation is null)
        {
            await PurgeExpiredRowsAsync(now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (invitation.DeliveryDiscardAfter <= now || invitation.AttemptCount >= 20)
        {
            MarkDeliveryFailed(invitation, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        string token;
        try
        {
            token = dataProtectionProvider
                .CreateProtector("OriSync.MentorInvitationToken.v1")
                .Unprotect(invitation.ProtectedToken ?? string.Empty);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogTokenProtectionFailure(logger, invitation.Id, exception);
            MarkDeliveryFailed(invitation, now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var baseUrl = options.Value.PublicBaseUrl!.TrimEnd('/');
        var activationLink = $"{baseUrl}/activate-mentor#invitation={invitation.Id}" +
            $"&token={Uri.EscapeDataString(token)}";
        var sent = await emailSender.SendAsync(
            invitation.Email,
            activationLink,
            $"mentor-invitation/{invitation.Id}/{invitation.DeliveryVersion}",
            cancellationToken);
        var completedAt = timeProvider.GetUtcNow();
        if (!sent)
        {
            ScheduleRetry(invitation, completedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        invitation.SentAt = completedAt;
        invitation.ExpiresAt = completedAt + InvitationLifetime;
        invitation.ProtectedToken = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task PurgeExpiredRowsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await dbContext.MentorInvitations
            .Where(item =>
                item.ExpiresAt != null &&
                item.ExpiresAt <= now &&
                item.AcceptedAt == null &&
                item.CancelledAt == null &&
                item.PurgeAfter == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.PurgeAfter,
                    item => item.ExpiresAt!.Value + RetentionPeriod),
                cancellationToken);
        _ = await dbContext.MentorInvitations
            .Where(item => item.PurgeAfter != null && item.PurgeAfter <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void MarkDeliveryFailed(MentorInvitation invitation, DateTimeOffset now)
    {
        invitation.DeliveryFailedAt = now;
        invitation.ProtectedToken = null;
        invitation.PurgeAfter = now + RetentionPeriod;
    }

    private static void ScheduleRetry(MentorInvitation invitation, DateTimeOffset now)
    {
        invitation.AttemptCount++;
        var exponent = Math.Min(invitation.AttemptCount - 1, 4);
        var delaySeconds = Math.Min(5 * (1 << exponent), 60);
        invitation.AvailableAt = now + TimeSpan.FromSeconds(delaySeconds);
    }

    [LoggerMessage(
        LogLevel.Error,
        "Mentor-invitation {InvitationId} token could not be unprotected.")]
    private static partial void LogTokenProtectionFailure(
        ILogger logger,
        long invitationId,
        Exception exception);
}
