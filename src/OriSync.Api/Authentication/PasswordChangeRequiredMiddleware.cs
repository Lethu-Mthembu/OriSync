namespace OriSync.Api.Authentication;

public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var mustChangePassword = context.User.FindFirst(
            AuthenticationConstants.MustChangePasswordClaim)?.Value;
        var isOperationalApi = context.Request.Path.StartsWithSegments("/api") &&
            !context.Request.Path.StartsWithSegments("/api/auth") &&
            !context.Request.Path.StartsWithSegments("/api/status");

        if (context.User.Identity?.IsAuthenticated == true &&
            isOperationalApi &&
            string.Equals(mustChangePassword, "true", StringComparison.Ordinal))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Password change required.",
                detail: "Change the temporary password before using OriSync.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
