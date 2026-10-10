using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.Authentication;

public static class AdminBootstrapCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var email = configuration["Bootstrap:AdminEmail"]?.Trim();
        var password = configuration["Bootstrap:AdminPassword"];
        var firstName = configuration["Bootstrap:AdminFirstName"]?.Trim();
        var surname = configuration["Bootstrap:AdminSurname"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) ||
            !System.Net.Mail.MailAddress.TryCreate(email, out _) ||
            !PasswordRules.IsValidTemporary(password) ||
            string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(surname))
        {
            await output.WriteLineAsync(
                "Bootstrap configuration is invalid. Set Bootstrap__AdminEmail, " +
                "Bootstrap__AdminPassword, Bootstrap__AdminFirstName and Bootstrap__AdminSurname.");
            return 2;
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
        if (await dbContext.Accounts.AnyAsync(
                account => account.Role == AccountRole.Admin,
                cancellationToken))
        {
            await output.WriteLineAsync("Bootstrap refused: an admin account already exists.");
            return 3;
        }

        var now = timeProvider.GetUtcNow();
        var recoveryCode = CredentialSecrets.GenerateOpaqueToken();
        var person = new Person
        {
            PersonType = PersonType.Admin,
            FirstName = firstName,
            Surname = surname,
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = email,
            NormalizedEmail = email.ToLowerInvariant(),
            CreatedAt = now
        });

        var account = new Account
        {
            Person = person,
            Role = AccountRole.Admin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = now,
            PasswordChangedAt = now,
            RecoveryCodeHash = CredentialSecrets.Hash(recoveryCode),
            RecoveryCodeIssuedAt = now
        };
        account.PasswordHash = passwordHasher.HashPassword(account, password!);

        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);

        await output.WriteLineAsync("Admin bootstrap completed.");
        await output.WriteLineAsync($"Recovery code (shown once): {recoveryCode}");
        return 0;
    }

    public static async Task<int> RotateRecoveryCodeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var email = configuration["Bootstrap:AdminEmail"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
        {
            await output.WriteLineAsync("Set Bootstrap__AdminEmail before rotating recovery.");
            return 2;
        }

        var account = await dbContext.Accounts
            .Include(item => item.Person)
                .ThenInclude(person => person.Emails)
            .SingleOrDefaultAsync(
                item => item.Role == AccountRole.Admin &&
                    item.Person.Emails.Any(personEmail =>
                        personEmail.EmailType == EmailType.Login &&
                        personEmail.NormalizedEmail == email),
                cancellationToken);
        if (account is null)
        {
            await output.WriteLineAsync("No matching admin account exists.");
            return 3;
        }

        var now = timeProvider.GetUtcNow();
        var recoveryCode = CredentialSecrets.GenerateOpaqueToken();
        account.RecoveryCodeHash = CredentialSecrets.Hash(recoveryCode);
        account.RecoveryCodeIssuedAt = now;
        await dbContext.Sessions
            .Where(session => session.AccountId == account.Id && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(session => session.RevokedAt, now)
                    .SetProperty(session => session.RevocationReason, "Admin recovery rotated."),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await output.WriteLineAsync($"Replacement recovery code (shown once): {recoveryCode}");
        return 0;
    }
}
