using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.Authentication;

public sealed partial class AuthenticationService(
    OriSyncDbContext dbContext,
    IPasswordHasher<Account> passwordHasher,
    IPasswordHasher<PasswordResetOtp> otpHasher,
    TimeProvider timeProvider,
    IPasswordResetCodeGenerator passwordResetCodeGenerator,
    IOptions<PasswordResetOptions> passwordResetOptions,
    ILogger<AuthenticationService> logger)
{
    public async Task<LoginResult?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var accountId = await dbContext.Accounts
            .Where(account =>
                account.Person.Emails.Any(personEmail =>
                    personEmail.EmailType == EmailType.Login &&
                    personEmail.NormalizedEmail == normalizedEmail))
            .Select(account => (long?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // Serialize logins for the same account. Without this row lock, two successful
        // requests could race against the one-active-session database constraint.
        var account = await dbContext.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {accountId.Value} FOR UPDATE")
            .Include(item => item.Person)
            .SingleAsync(cancellationToken);

        if (!account.IsActive)
        {
            return null;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            account,
            account.PasswordHash,
            password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        await RevokeSessionsAsync(account.Id, now, "Replaced by a new login.", cancellationToken);

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = passwordHasher.HashPassword(account, password);
        }

        var rawToken = CredentialSecrets.GenerateOpaqueToken();
        var session = new AccountSession
        {
            AccountId = account.Id,
            TokenHash = CredentialSecrets.Hash(rawToken),
            CreatedAt = now,
            LastSeenAt = now,
            AbsoluteExpiresAt = now + AuthenticationConstants.AbsoluteSessionLifetime
        };

        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LoginResult(session, account, rawToken);
    }

    public async Task<bool> ChangePasswordAsync(
        long accountId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var account = await LockAccountAsync(accountId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            return false;
        }

        var verification = passwordHasher.VerifyHashedPassword(
            account,
            account.PasswordHash,
            currentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = now;
        account.MustChangePassword = false;
        await RevokeSessionsAsync(account.Id, now, "Password changed.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RequestMentorPasswordResetAsync(
        string mentorNumber,
        string email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var accountId = await dbContext.Accounts
            .Where(account =>
                account.Role == AccountRole.Mentor &&
                account.Person.InstitutionNumber == mentorNumber &&
                account.Person.Emails.Any(personEmail =>
                    personEmail.EmailType == EmailType.Login &&
                    personEmail.NormalizedEmail == normalizedEmail))
            .Select(account => (long?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is null)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var account = await LockAccountAsync(accountId.Value, cancellationToken);
        if (account is null || !account.IsActive || account.Role != AccountRole.Mentor)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var resetRequests = dbContext.PasswordResetOtps
            .Where(otp => otp.AccountId == account.Id);
        var latestRequestedAt = await resetRequests
            .MaxAsync(otp => (DateTimeOffset?)otp.RequestedAt, cancellationToken);
        if (latestRequestedAt > now - PasswordResetConstants.ResendCooldown ||
            await resetRequests.CountAsync(
                otp => otp.RequestedAt >= now - PasswordResetConstants.RequestWindow,
                cancellationToken) >= PasswordResetConstants.MaximumRequestsPerWindow)
        {
            return;
        }

        if (!passwordResetCodeGenerator.TryCreate(out var nonce, out var code))
        {
            LogInvalidCodeConfiguration(logger);
            return;
        }

        var recipient = await dbContext.PersonEmails
            .Where(personEmail =>
                personEmail.PersonId == account.PersonId &&
                personEmail.EmailType == EmailType.Login)
            .Select(personEmail => new { personEmail.Email, personEmail.Person.FirstName })
            .SingleAsync(cancellationToken);
        var otp = new PasswordResetOtp
        {
            AccountId = account.Id,
            CodeNonce = nonce,
            RequestedAt = now
        };
        otp.CodeHash = otpHasher.HashPassword(otp, code);
        var outbox = new PasswordResetEmailOutbox
        {
            PasswordResetOtp = otp,
            RecipientEmail = recipient.Email,
            RecipientFirstName = recipient.FirstName,
            CreatedAt = now,
            AvailableAt = now,
            DiscardAfter = now + PasswordResetConstants.DeliveryWindow
        };
        dbContext.PasswordResetEmailOutbox.Add(outbox);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> CompleteMentorPasswordResetAsync(
        string mentorNumber,
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var trimmedCode = code.Trim();
        var configuredLength = passwordResetOptions.Value.CodeLength;
        if (configuredLength is < 4 or > 9 ||
            trimmedCode.Length != configuredLength ||
            trimmedCode.Any(character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var accountId = await dbContext.Accounts
            .Where(account =>
                account.Role == AccountRole.Mentor &&
                account.Person.InstitutionNumber == mentorNumber &&
                account.Person.Emails.Any(personEmail =>
                    personEmail.EmailType == EmailType.Login &&
                    personEmail.NormalizedEmail == normalizedEmail))
            .Select(account => (long?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (accountId is null)
        {
            return false;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var account = await LockAccountAsync(accountId.Value, cancellationToken);
        if (account is null || !account.IsActive || account.Role != AccountRole.Mentor)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var otp = await dbContext.PasswordResetOtps
            .Where(item =>
                item.AccountId == account.Id &&
                item.SentAt != null &&
                item.ConsumedAt == null)
            .OrderByDescending(item => item.SentAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (otp is null)
        {
            return false;
        }

        if (otp.ExpiresAt is null ||
            otp.ExpiresAt <= now ||
            otp.FailedAttempts >= PasswordResetConstants.MaximumVerificationAttempts)
        {
            otp.ConsumedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (otpHasher.VerifyHashedPassword(otp, otp.CodeHash, trimmedCode) ==
            PasswordVerificationResult.Failed)
        {
            otp.FailedAttempts++;
            if (otp.FailedAttempts >= PasswordResetConstants.MaximumVerificationAttempts)
            {
                otp.ConsumedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = now;
        account.MustChangePassword = false;
        await RevokeSessionsAsync(account.Id, now, "Password reset by email OTP.", cancellationToken);
        await dbContext.PasswordResetOtps
            .Where(item => item.AccountId == account.Id && item.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ConsumedAt, now),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<string?> RecoverAdminAsync(
        string email,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var accountId = await dbContext.Accounts
            .Where(account =>
                account.Role == AccountRole.Admin &&
                account.Person.Emails.Any(personEmail =>
                    personEmail.EmailType == EmailType.Login &&
                    personEmail.NormalizedEmail == normalizedEmail))
            .Select(account => (long?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var account = await LockAccountAsync(accountId.Value, cancellationToken);
        if (account is null ||
            !account.IsActive ||
            account.Role != AccountRole.Admin ||
            string.IsNullOrWhiteSpace(account.RecoveryCodeHash) ||
            !CredentialSecrets.FixedTimeEquals(recoveryCode, account.RecoveryCodeHash))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var replacementRecoveryCode = CredentialSecrets.GenerateOpaqueToken();
        account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = now;
        account.MustChangePassword = false;
        account.RecoveryCodeHash = CredentialSecrets.Hash(replacementRecoveryCode);
        account.RecoveryCodeIssuedAt = now;
        await RevokeSessionsAsync(account.Id, now, "Admin account recovered.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replacementRecoveryCode;
    }

    public async Task<bool> RevokeSessionAsync(
        long sessionId,
        long accountId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await dbContext.Sessions
            .Where(session =>
                session.Id == sessionId &&
                session.AccountId == accountId &&
                session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, now)
                    .SetProperty(session => session.RevocationReason, reason),
                cancellationToken) == 1;
    }

    public async Task<bool> RecordActivityAsync(
        long sessionId,
        long accountId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var inactiveCutoff = now - AuthenticationConstants.InactivityTimeout;
        return await dbContext.Sessions
            .Where(session =>
                session.Id == sessionId &&
                session.AccountId == accountId &&
                session.RevokedAt == null &&
                session.AbsoluteExpiresAt > now &&
                session.LastSeenAt > inactiveCutoff)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.LastSeenAt, now),
                cancellationToken) == 1;
    }

    public async Task<SessionResponse?> GetSessionAsync(
        long accountId,
        long sessionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId && session.AccountId == accountId)
            .Select(session => new SessionResponse(
                session.Account.Id,
                session.Account.Person.FirstName,
                session.Account.Person.Surname,
                session.Account.Role.ToString(),
                session.Account.CurrentGroupId,
                session.Account.MustChangePassword,
                session.AbsoluteExpiresAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<Account?> LockAccountAsync(long accountId, CancellationToken cancellationToken) =>
        dbContext.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private Task<int> RevokeSessionsAsync(
        long accountId,
        DateTimeOffset revokedAt,
        string reason,
        CancellationToken cancellationToken) =>
        dbContext.Sessions
            .Where(session => session.AccountId == accountId && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, revokedAt)
                    .SetProperty(session => session.RevocationReason, reason),
                cancellationToken);

    [LoggerMessage(
        LogLevel.Error,
        "Password reset code generation is not configured with a valid length and secret.")]
    private static partial void LogInvalidCodeConfiguration(ILogger logger);
}
