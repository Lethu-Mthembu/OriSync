using System.Data;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.MentorManagement;

public sealed partial class MentorManagementService(
    OriSyncDbContext dbContext,
    IPasswordHasher<Account> passwordHasher,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan AuditRetention = TimeSpan.FromDays(14);
    private static readonly TimeSpan InvitationRetention = TimeSpan.FromDays(14);
    private static readonly int[] AllowedPageSizes = [20, 50, 100];

    public async Task<MentorDirectoryResponse> GetDirectoryAsync(
        string? search,
        long? groupId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || !AllowedPageSizes.Contains(pageSize))
        {
            throw Invalid("Page must be positive and page size must be 20, 50 or 100.");
        }

        var now = timeProvider.GetUtcNow();
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(item => item.Role == AccountRole.Mentor)
            .Include(item => item.Person)
                .ThenInclude(person => person.Emails)
            .Include(item => item.CurrentGroup)
            .ToListAsync(cancellationToken);
        var invitations = await dbContext.MentorInvitations
            .AsNoTracking()
            .Where(item => item.AcceptedAt == null)
            .Include(item => item.Group)
            .ToListAsync(cancellationToken);

        var rows = accounts.Select(account => new MentorDirectoryItemResponse(
                account.Id,
                "Mentor",
                account.IsActive ? "Active" : "Disabled",
                account.Person.Emails.Single(item => item.EmailType == EmailType.Login).Email,
                account.Person.InstitutionNumber,
                account.Person.FirstName,
                account.Person.Surname,
                account.CurrentGroupId ?? 0,
                account.CurrentGroup?.Name ?? "UNASSIGNED",
                account.CurrentGroup?.BadgeColor ?? "#64748B",
                account.CreatedAt,
                account.DeletionEligibleAt,
                !account.IsActive && account.DeletionEligibleAt <= now))
            .Concat(invitations.Select(invitation => new MentorDirectoryItemResponse(
                invitation.Id,
                "Invitation",
                GetInvitationStatus(invitation, now),
                invitation.Email,
                null,
                null,
                null,
                invitation.GroupId,
                invitation.Group.Name,
                invitation.Group.BadgeColor,
                invitation.CreatedAt,
                null,
                false)));

        if (groupId is not null)
        {
            rows = rows.Where(item => item.GroupId == groupId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(item => string.Equals(
                item.Status,
                status.Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(item =>
                Contains(item.Email, term) ||
                Contains(item.MentorNumber, term) ||
                Contains(item.FirstName, term) ||
                Contains(item.Surname, term) ||
                Contains(item.GroupName, term));
        }

        var ordered = rows
            .OrderBy(item => item.Surname ?? "~", StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FirstName ?? item.Email, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToArray();
        var totalItems = ordered.Length;
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new MentorDirectoryResponse(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<MentorDirectoryItemResponse> CreateInvitationAsync(
        CreateMentorInvitationRequest request,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var email = ValidateEmail(request.Email);
        var normalizedEmail = email.ToLowerInvariant();
        var group = await GetAssignableGroupAsync(request.GroupId, cancellationToken);
        if (await dbContext.PersonEmails.AnyAsync(
                item => item.NormalizedEmail == normalizedEmail,
                cancellationToken))
        {
            throw Conflict("This email already belongs to an OriSync account.");
        }

        var openInvitation = await dbContext.MentorInvitations.AnyAsync(
            item => item.NormalizedEmail == normalizedEmail &&
                item.AcceptedAt == null &&
                item.CancelledAt == null,
            cancellationToken);
        if (openInvitation)
        {
            throw Conflict("This email already has an invitation. Resend or update that invitation.");
        }

        var now = timeProvider.GetUtcNow();
        var rawToken = CredentialSecrets.GenerateOpaqueToken();
        var invitation = new MentorInvitation
        {
            GroupId = group.Id,
            Email = email,
            NormalizedEmail = normalizedEmail,
            TokenHash = CredentialSecrets.Hash(rawToken),
            ProtectedToken = ProtectToken(rawToken),
            CreatedAt = now,
            AvailableAt = now,
            DeliveryDiscardAfter = now + DeliveryWindow,
            DeliveryVersion = 1
        };
        dbContext.MentorInvitations.Add(invitation);
        AddAudit(
            actorAccountId,
            "MentorInvitationCreated",
            "MentorInvitation",
            null,
            new { email = normalizedEmail, groupId = group.Id },
            now);
        await SaveChangesAsync("The mentor invitation could not be created.", cancellationToken);
        return MapInvitation(invitation, group, now);
    }

    public async Task<MentorDirectoryItemResponse> ChangeInvitationGroupAsync(
        long invitationId,
        ChangeMentorInvitationGroupRequest request,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var invitation = await FindPendingInvitationAsync(invitationId, cancellationToken);
        var group = await GetAssignableGroupAsync(request.GroupId, cancellationToken);
        invitation.GroupId = group.Id;
        var now = timeProvider.GetUtcNow();
        AddAudit(
            actorAccountId,
            "MentorInvitationGroupChanged",
            "MentorInvitation",
            invitation.Id,
            new { groupId = group.Id },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapInvitation(invitation, group, now);
    }

    public async Task<MentorDirectoryItemResponse> ResendInvitationAsync(
        long invitationId,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var invitation = await FindPendingInvitationAsync(invitationId, cancellationToken);
        _ = await GetAssignableGroupAsync(invitation.GroupId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var rawToken = CredentialSecrets.GenerateOpaqueToken();
        invitation.TokenHash = CredentialSecrets.Hash(rawToken);
        invitation.ProtectedToken = ProtectToken(rawToken);
        invitation.AvailableAt = now;
        invitation.DeliveryDiscardAfter = now + DeliveryWindow;
        invitation.DeliveryVersion++;
        invitation.AttemptCount = 0;
        invitation.SentAt = null;
        invitation.ExpiresAt = null;
        invitation.DeliveryFailedAt = null;
        invitation.PurgeAfter = null;
        AddAudit(
            actorAccountId,
            "MentorInvitationResent",
            "MentorInvitation",
            invitation.Id,
            new { invitation.GroupId },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapInvitation(invitation, invitation.Group, now);
    }

    public async Task CancelInvitationAsync(
        long invitationId,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var invitation = await FindPendingInvitationAsync(invitationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        invitation.CancelledAt = now;
        invitation.ProtectedToken = null;
        invitation.PurgeAfter = now + InvitationRetention;
        AddAudit(
            actorAccountId,
            "MentorInvitationCancelled",
            "MentorInvitation",
            invitation.Id,
            new { invitation.GroupId },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MentorInvitationValidationResponse> ValidateInvitationAsync(
        ValidateMentorInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var invitation = await dbContext.MentorInvitations
            .AsNoTracking()
            .Include(item => item.Group)
                .ThenInclude(group => group.Orientation)
            .SingleOrDefaultAsync(item => item.Id == request.InvitationId, cancellationToken);
        EnsureInvitationCanBeAccepted(invitation, request.Token);
        return new MentorInvitationValidationResponse(
            invitation!.Email,
            invitation.GroupId,
            invitation.Group.Name,
            invitation.Group.BadgeColor,
            invitation.ExpiresAt!.Value);
    }

    public async Task AcceptInvitationAsync(
        AcceptMentorInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateMentorIdentity(request.FirstName, request.Surname, request.MentorNumber);
        ValidatePassword(request.Password);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var rows = await dbContext.MentorInvitations
            .FromSqlInterpolated($"""
                SELECT * FROM mentor_invitations
                WHERE id = {request.InvitationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var invitation = rows.SingleOrDefault();
        if (invitation is not null)
        {
            await dbContext.Entry(invitation)
                .Reference(item => item.Group)
                .Query()
                .Include(group => group.Orientation)
                .LoadAsync(cancellationToken);
        }

        EnsureInvitationCanBeAccepted(invitation, request.Token);
        var acceptedInvitation = invitation!;
        if (await dbContext.People.AnyAsync(
                item => item.InstitutionNumber == request.MentorNumber,
                cancellationToken))
        {
            throw Conflict("This mentor number already exists.");
        }

        if (await dbContext.PersonEmails.AnyAsync(
                item => item.NormalizedEmail == acceptedInvitation.NormalizedEmail,
                cancellationToken))
        {
            throw Conflict("This email already belongs to an OriSync account.");
        }

        var now = timeProvider.GetUtcNow();
        var person = new Person
        {
            PersonType = PersonType.Mentor,
            InstitutionNumber = request.MentorNumber,
            FirstName = request.FirstName.Trim(),
            Surname = request.Surname.Trim(),
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = acceptedInvitation.Email,
            NormalizedEmail = acceptedInvitation.NormalizedEmail,
            CreatedAt = now
        });
        var account = new Account
        {
            Person = person,
            Role = AccountRole.Mentor,
            CurrentGroupId = acceptedInvitation.GroupId,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            PasswordChangedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
        dbContext.Accounts.Add(account);
        acceptedInvitation.AcceptedAt = now;
        acceptedInvitation.ProtectedToken = null;
        acceptedInvitation.PurgeAfter = now + InvitationRetention;
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(
            null,
            "MentorInvitationAccepted",
            "Account",
            account.Id,
            new { invitationId = acceptedInvitation.Id, acceptedInvitation.GroupId },
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MentorDirectoryItemResponse> UpdateMentorAsync(
        long accountId,
        UpdateMentorRequest request,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        ValidateMentorIdentity(request.FirstName, request.Surname, request.MentorNumber);
        var email = ValidateEmail(request.Email);
        var normalizedEmail = email.ToLowerInvariant();
        var group = await GetAssignableGroupAsync(request.GroupId, cancellationToken);
        var account = await dbContext.Accounts
            .Include(item => item.Person)
                .ThenInclude(person => person.Emails)
            .Include(item => item.CurrentGroup)
            .SingleOrDefaultAsync(
                item => item.Id == accountId && item.Role == AccountRole.Mentor,
                cancellationToken) ?? throw NotFound("Mentor not found.");
        if (await dbContext.People.AnyAsync(
                item => item.InstitutionNumber == request.MentorNumber && item.Id != account.PersonId,
                cancellationToken))
        {
            throw Conflict("This mentor number already exists.");
        }

        var loginEmail = account.Person.Emails.Single(item => item.EmailType == EmailType.Login);
        if (await dbContext.PersonEmails.AnyAsync(
                item => item.NormalizedEmail == normalizedEmail && item.Id != loginEmail.Id,
                cancellationToken) ||
            await dbContext.MentorInvitations.AnyAsync(
                item => item.NormalizedEmail == normalizedEmail &&
                    item.AcceptedAt == null &&
                    item.CancelledAt == null,
                cancellationToken))
        {
            throw Conflict("This email is already in use.");
        }

        var now = timeProvider.GetUtcNow();
        account.Person.FirstName = request.FirstName.Trim();
        account.Person.Surname = request.Surname.Trim();
        account.Person.InstitutionNumber = request.MentorNumber;
        loginEmail.Email = email;
        loginEmail.NormalizedEmail = normalizedEmail;
        account.CurrentGroupId = group.Id;
        await RevokeSessionsAsync(account.Id, now, "Mentor profile changed by administrator.", cancellationToken);
        AddAudit(
            actorAccountId,
            "MentorUpdated",
            "Account",
            account.Id,
            new { groupId = group.Id },
            now);
        await SaveChangesAsync("The mentor could not be updated.", cancellationToken);
        account.CurrentGroup = group;
        return MapAccount(account, now);
    }

    public async Task DisableMentorAsync(
        long accountId,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var account = await FindMentorAsync(accountId, cancellationToken);
        if (!account.IsActive)
        {
            throw Conflict("The mentor is already disabled.");
        }

        var now = timeProvider.GetUtcNow();
        account.IsActive = false;
        account.DisabledAt = now;
        account.DeletionEligibleAt = now.AddMonths(6);
        await RevokeSessionsAsync(account.Id, now, "Mentor disabled by administrator.", cancellationToken);
        AddAudit(actorAccountId, "MentorDisabled", "Account", account.Id, null, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateMentorAsync(
        long accountId,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var account = await FindMentorAsync(accountId, cancellationToken);
        if (account.IsActive)
        {
            throw Conflict("The mentor is already active.");
        }

        if (account.CurrentGroupId is null)
        {
            throw Conflict("Assign the mentor to an active group before reactivation.");
        }

        _ = await GetAssignableGroupAsync(account.CurrentGroupId.Value, cancellationToken);
        var now = timeProvider.GetUtcNow();
        account.IsActive = true;
        account.DisabledAt = null;
        account.DeletionEligibleAt = null;
        AddAudit(actorAccountId, "MentorReactivated", "Account", account.Id, null, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMentorAsync(
        long accountId,
        long actorAccountId,
        CancellationToken cancellationToken)
    {
        var account = await FindMentorAsync(accountId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (account.IsActive ||
            account.DeletionEligibleAt is null ||
            account.DeletionEligibleAt > now)
        {
            throw Conflict("The mentor can only be deleted after six months disabled.");
        }

        var person = account.Person;
        AddAudit(actorAccountId, "MentorDeleted", "Account", account.Id, null, now);
        dbContext.People.Remove(person);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrientationGroup> GetAssignableGroupAsync(
        long groupId,
        CancellationToken cancellationToken) =>
        await dbContext.Groups
            .Include(item => item.Orientation)
            .SingleOrDefaultAsync(
                item => item.Id == groupId && item.IsActive && item.Orientation.IsActive,
                cancellationToken) ?? throw Conflict(
                "Mentors can only be assigned to an active group in the active orientation.");

    private async Task<MentorInvitation> FindPendingInvitationAsync(
        long invitationId,
        CancellationToken cancellationToken) =>
        await dbContext.MentorInvitations
            .Include(item => item.Group)
            .SingleOrDefaultAsync(
                item => item.Id == invitationId &&
                    item.AcceptedAt == null &&
                    item.CancelledAt == null,
                cancellationToken) ?? throw NotFound("Pending mentor invitation not found.");

    private async Task<Account> FindMentorAsync(
        long accountId,
        CancellationToken cancellationToken) =>
        await dbContext.Accounts
            .Include(item => item.Person)
                .ThenInclude(person => person.Emails)
            .Include(item => item.CurrentGroup)
            .SingleOrDefaultAsync(
                item => item.Id == accountId && item.Role == AccountRole.Mentor,
                cancellationToken) ?? throw NotFound("Mentor not found.");

    private void EnsureInvitationCanBeAccepted(MentorInvitation? invitation, string token)
    {
        var now = timeProvider.GetUtcNow();
        if (invitation is null ||
            invitation.AcceptedAt is not null ||
            invitation.CancelledAt is not null ||
            invitation.SentAt is null ||
            invitation.ExpiresAt is null ||
            invitation.ExpiresAt <= now ||
            invitation.Group is null ||
            !invitation.Group.IsActive ||
            !invitation.Group.Orientation.IsActive ||
            string.IsNullOrWhiteSpace(token) ||
            !CredentialSecrets.FixedTimeEquals(token, invitation.TokenHash))
        {
            throw InvalidInvitation();
        }
    }

    private static void ValidateMentorIdentity(
        string firstName,
        string surname,
        string mentorNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(surname) || surname.Trim().Length > 100)
        {
            throw Invalid("First name and surname are required and cannot exceed 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(mentorNumber) ||
            !MentorNumberPattern().IsMatch(mentorNumber))
        {
            throw Invalid("The mentor number must contain nine digits and start with 2.");
        }

    }

    private static void ValidatePassword(string password)
    {
        if (!PasswordRules.IsValid(password))
        {
            throw Invalid(PasswordRules.Requirements);
        }
    }

    private static string ValidateEmail(string? value)
    {
        var email = value?.Trim() ?? string.Empty;
        if (email.Length > 320 ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Enter a valid email address.");
        }

        return parsed.Address;
    }

    private string ProtectToken(string token) => dataProtectionProvider
        .CreateProtector("OriSync.MentorInvitationToken.v1")
        .Protect(token);

    private async Task RevokeSessionsAsync(
        long accountId,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken) =>
        _ = await dbContext.Sessions
            .Where(item => item.AccountId == accountId && item.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.RevokedAt, now)
                    .SetProperty(item => item.RevocationReason, reason),
                cancellationToken);

    private void AddAudit(
        long? actorAccountId,
        string eventType,
        string entityType,
        long? entityId,
        object? details,
        DateTimeOffset now) =>
        dbContext.AuditEvents.Add(new AuditEvent
        {
            AccountId = actorAccountId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            OccurredAt = now,
            ExpiresAt = now + AuditRetention
        });

    private async Task SaveChangesAsync(string detail, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw Conflict(detail);
        }
    }

    private static MentorDirectoryItemResponse MapAccount(
        Account account,
        DateTimeOffset now) => new(
        account.Id,
        "Mentor",
        account.IsActive ? "Active" : "Disabled",
        account.Person.Emails.Single(item => item.EmailType == EmailType.Login).Email,
        account.Person.InstitutionNumber,
        account.Person.FirstName,
        account.Person.Surname,
        account.CurrentGroupId ?? 0,
        account.CurrentGroup?.Name ?? "UNASSIGNED",
        account.CurrentGroup?.BadgeColor ?? "#64748B",
        account.CreatedAt,
        account.DeletionEligibleAt,
        !account.IsActive && account.DeletionEligibleAt <= now);

    private static MentorDirectoryItemResponse MapInvitation(
        MentorInvitation invitation,
        OrientationGroup group,
        DateTimeOffset now) => new(
        invitation.Id,
        "Invitation",
        GetInvitationStatus(invitation, now),
        invitation.Email,
        null,
        null,
        null,
        invitation.GroupId,
        group.Name,
        group.BadgeColor,
        invitation.CreatedAt,
        null,
        false);

    private static string GetInvitationStatus(
        MentorInvitation invitation,
        DateTimeOffset now)
    {
        if (invitation.CancelledAt is not null)
        {
            return "Cancelled";
        }

        if (invitation.DeliveryFailedAt is not null)
        {
            return "Delivery failed";
        }

        if (invitation.SentAt is null)
        {
            return "Pending delivery";
        }

        return invitation.ExpiresAt <= now ? "Expired" : "Invited";
    }

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static MentorManagementException Invalid(string detail) => new(
        StatusCodes.Status400BadRequest,
        "Invalid mentor data.",
        detail);

    private static MentorManagementException InvalidInvitation() => new(
        StatusCodes.Status400BadRequest,
        "Mentor activation failed.",
        "The activation link is invalid, expired or no longer usable.");

    private static MentorManagementException Conflict(string detail) => new(
        StatusCodes.Status409Conflict,
        "Mentor management conflict.",
        detail);

    private static MentorManagementException NotFound(string detail) => new(
        StatusCodes.Status404NotFound,
        "Mentor record not found.",
        detail);

    [GeneratedRegex("^2[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MentorNumberPattern();
}
