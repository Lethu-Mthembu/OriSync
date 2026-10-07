namespace OriSync.Api.Authentication;

public static class SessionCookie
{
    public static void Append(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        response.Cookies.Append(
            AuthenticationConstants.SessionCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = expiresAt,
                IsEssential = true
            });
    }

    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(
            AuthenticationConstants.SessionCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true
            });
    }
}
