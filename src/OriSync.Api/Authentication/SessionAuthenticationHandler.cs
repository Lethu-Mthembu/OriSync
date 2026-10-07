using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OriSync.Api.Data;

namespace OriSync.Api.Authentication;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    OriSyncDbContext dbContext,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(
                AuthenticationConstants.SessionCookieName,
                out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var tokenHash = CredentialSecrets.Hash(token);
        var session = await dbContext.Sessions
            .AsNoTracking()
            .Include(item => item.Account)
                .ThenInclude(account => account.Person)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.Fail("The session is invalid.");
        }

        var now = timeProvider.GetUtcNow();
        var inactiveAt = session.LastSeenAt + AuthenticationConstants.InactivityTimeout;
        if (session.RevokedAt is not null ||
            !session.Account.IsActive ||
            session.AbsoluteExpiresAt <= now ||
            inactiveAt <= now)
        {
            return AuthenticateResult.Fail("The session has expired.");
        }

        var account = session.Account;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, $"{account.Person.FirstName} {account.Person.Surname}"),
            new(ClaimTypes.Role, account.Role.ToString()),
            new(
                AuthenticationConstants.SessionIdClaim,
                session.Id.ToString(CultureInfo.InvariantCulture)),
            new(
                AuthenticationConstants.MustChangePasswordClaim,
                account.MustChangePassword.ToString(CultureInfo.InvariantCulture).ToLowerInvariant())
        };

        if (account.CurrentGroupId is { } groupId)
        {
            claims.Add(new Claim(
                AuthenticationConstants.GroupIdClaim,
                groupId.ToString(CultureInfo.InvariantCulture)));
        }

        var identity = new ClaimsIdentity(claims, AuthenticationConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, AuthenticationConstants.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        SessionCookie.Delete(Response);
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
