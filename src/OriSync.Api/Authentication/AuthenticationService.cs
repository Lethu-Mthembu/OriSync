using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.Authentication;

public sealed class AuthenticationService(
    OriSyncDbContext dbContext,
    IPasswordHasher<Account> passwordHasher,
    TimeProvider timeProvider)
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

    public async Task<bool> ResetMentorPasswordAsync(
        string mentorNumber,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var accountId = await dbContext.Accounts
            .Where(account =>
                account.Role == AccountRole.Mentor &&
                account.Person.InstitutionNumber == mentorNumber)
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
        account.PasswordHash = passwordHasher.HashPassword(account, newPassword);
        account.PasswordChangedAt = now;
        account.MustChangePassword = false;
        await RevokeSessionsAsync(account.Id, now, "Password reset.", cancellationToken);
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
}
