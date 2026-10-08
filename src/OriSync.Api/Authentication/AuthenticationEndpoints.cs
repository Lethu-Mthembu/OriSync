using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;

namespace OriSync.Api.Authentication;

public static partial class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var authentication = endpoints.MapGroup("/api/auth")
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        authentication.MapGet("/csrf", GetCsrfToken)
            .AllowAnonymous();
        authentication.MapPost("/login", LoginAsync)
            .AllowAnonymous();
        authentication.MapGet("/session", GetSessionAsync)
            .RequireAuthorization();
        authentication.MapPost("/activity", RecordActivityAsync)
            .RequireAuthorization();
        authentication.MapPost("/logout", LogoutAsync)
            .RequireAuthorization();
        authentication.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization();
        authentication.MapPost("/mentor-password-reset/request", RequestMentorPasswordResetAsync)
            .AllowAnonymous();
        authentication.MapPost("/mentor-password-reset/complete", CompleteMentorPasswordResetAsync)
            .AllowAnonymous();
        authentication.MapPost("/recover-admin", RecoverAdminAsync)
            .AllowAnonymous();

        return endpoints;
    }

    private static IResult GetCsrfToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new CsrfTokenResponse(tokens.RequestToken!));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthenticationService authenticationService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!IsEmail(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        var login = await authenticationService.LoginAsync(
            request.Email,
            request.Password,
            cancellationToken);
        if (login is null)
        {
            return InvalidCredentials();
        }

        SessionCookie.Append(
            context.Response,
            login.Token,
            login.Session.AbsoluteExpiresAt);

        return Results.Ok(new SessionResponse(
            login.Account.Id,
            login.Account.Person.FirstName,
            login.Account.Person.Surname,
            login.Account.Role.ToString(),
            login.Account.CurrentGroupId,
            login.Account.MustChangePassword,
            login.Session.AbsoluteExpiresAt));
    }

    private static async Task<IResult> GetSessionAsync(
        ClaimsPrincipal principal,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(principal, out var accountId, out var sessionId))
        {
            return Results.Unauthorized();
        }

        var session = await authenticationService.GetSessionAsync(
            accountId,
            sessionId,
            cancellationToken);
        return session is null ? Results.Unauthorized() : Results.Ok(session);
    }

    private static async Task<IResult> RecordActivityAsync(
        ClaimsPrincipal principal,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(principal, out var accountId, out var sessionId))
        {
            return Results.Unauthorized();
        }

        var recorded = await authenticationService.RecordActivityAsync(
            sessionId,
            accountId,
            cancellationToken);
        return recorded ? Results.NoContent() : Results.Unauthorized();
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        AuthenticationService authenticationService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (TryGetIdentity(principal, out var accountId, out var sessionId))
        {
            await authenticationService.RevokeSessionAsync(
                sessionId,
                accountId,
                "User logged out.",
                cancellationToken);
        }

        SessionCookie.Delete(context.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        AuthenticationService authenticationService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(principal, out var accountId))
        {
            return Results.Unauthorized();
        }

        if (!PasswordRules.IsValid(request.NewPassword))
        {
            return PasswordValidationProblem();
        }

        var changed = await authenticationService.ChangePasswordAsync(
            accountId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        if (!changed)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Password change failed.",
                detail: "The current password is incorrect.");
        }

        SessionCookie.Delete(context.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestMentorPasswordResetAsync(
        RequestMentorPasswordResetRequest request,
        AuthenticationService authenticationService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        // The same response is returned for malformed, unknown, rate-limited and
        // delivery-failed requests so this endpoint cannot enumerate accounts.
        if (MentorNumberPattern().IsMatch(request.MentorNumber) && IsEmail(request.Email))
        {
            await authenticationService.RequestMentorPasswordResetAsync(
                request.MentorNumber,
                request.Email,
                cancellationToken);
        }

        var remainingDelay = PasswordResetConstants.MinimumRequestDuration -
            timeProvider.GetElapsedTime(startedAt);
        if (remainingDelay > TimeSpan.Zero)
        {
            await Task.Delay(remainingDelay, timeProvider, cancellationToken);
        }

        return Results.Accepted(value: new PasswordResetRequestResponse(
            "If the mentor account exists, a password reset code was sent."));
    }

    private static async Task<IResult> CompleteMentorPasswordResetAsync(
        CompleteMentorPasswordResetRequest request,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        if (!PasswordRules.IsValid(request.NewPassword))
        {
            return PasswordValidationProblem();
        }

        if (!MentorNumberPattern().IsMatch(request.MentorNumber) ||
            !IsEmail(request.Email) ||
            string.IsNullOrWhiteSpace(request.Code))
        {
            return InvalidMentorPasswordReset();
        }

        var reset = await authenticationService.CompleteMentorPasswordResetAsync(
            request.MentorNumber,
            request.Email,
            request.Code,
            request.NewPassword,
            cancellationToken);
        return reset
            ? Results.NoContent()
            : InvalidMentorPasswordReset();
    }

    private static async Task<IResult> RecoverAdminAsync(
        RecoverAdminRequest request,
        AuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        if (!IsEmail(request.Email) ||
            string.IsNullOrWhiteSpace(request.RecoveryCode) ||
            !PasswordRules.IsValid(request.NewPassword))
        {
            return InvalidAdminRecovery();
        }

        var replacementCode = await authenticationService.RecoverAdminAsync(
            request.Email,
            request.RecoveryCode,
            request.NewPassword,
            cancellationToken);
        return replacementCode is null
            ? InvalidAdminRecovery()
            : Results.Ok(new AdminRecoveryResponse(replacementCode));
    }

    private static bool TryGetIdentity(
        ClaimsPrincipal principal,
        out long accountId,
        out long sessionId)
    {
        sessionId = 0;
        return TryGetAccountId(principal, out accountId) &&
            long.TryParse(
            principal.FindFirstValue(AuthenticationConstants.SessionIdClaim),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out sessionId);
    }

    private static bool TryGetAccountId(ClaimsPrincipal principal, out long accountId) =>
        long.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out accountId);

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value, out _);

    private static IResult InvalidCredentials() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Login failed.",
        detail: "The email or password is incorrect.");

    private static IResult PasswordValidationProblem() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid password.",
        detail: "Passwords must contain at least eight characters.");

    private static IResult InvalidAdminRecovery() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Admin recovery failed.",
        detail: "The email, recovery code or new password is invalid.");

    private static IResult InvalidMentorPasswordReset() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Password reset failed.",
        detail: "The reset code is invalid or has expired.");

    [GeneratedRegex("^2[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex MentorNumberPattern();
}
