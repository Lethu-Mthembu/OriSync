using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class AuthenticationEndpointsTests : IAsyncLifetime
{
    private const string OriginalPassword = "temporary-password";
    private const string AdminRecoveryCode = "admin-recovery-code";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_auth_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString()));

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedMentorAsync(dbContext, "223450001", "login@orisync.test");
        await SeedMentorAsync(dbContext, "223450002", "reset@orisync.test");
        await SeedMentorAsync(dbContext, "223450003", "change@orisync.test");
        await SeedMentorAsync(dbContext, "223450004", "expiry@orisync.test");
        await SeedAdminAsync(dbContext);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task LoginRequiresValidCsrfToken()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("login@orisync.test", OriginalPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReverseProxyHttpsSchemeAllowsSecureCsrfCookie()
    {
        using var client = _factory!.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost"),
                HandleCookies = false
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csrfCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains(AuthenticationConstants.AntiforgeryCookieName, csrfCookie, StringComparison.Ordinal);
        Assert.Contains("secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulLoginCreatesAuthenticatedSession()
    {
        using var client = CreateClient();

        var login = await LoginAsync(client, "login@orisync.test", OriginalPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = login.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains(AuthenticationConstants.SessionCookieName, sessionCookie, StringComparison.Ordinal);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var payload = await session.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Mentor", payload.Role);
        Assert.Equal("Test", payload.FirstName);
    }

    [Fact]
    public async Task NewLoginTerminatesPreviousSession()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(firstClient, "login@orisync.test", OriginalPassword)).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(secondClient, "login@orisync.test", OriginalPassword)).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await firstClient.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await secondClient.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task MentorNumberResetRevokesSessionAndReplacesPassword()
    {
        using var signedInClient = CreateClient();
        using var resetClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(signedInClient, "reset@orisync.test", OriginalPassword)).StatusCode);

        var csrf = await GetCsrfAsync(resetClient);
        using var resetRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/reset-mentor-password")
        {
            Content = JsonContent.Create(
                new ResetMentorPasswordRequest("223450002", "replacement-password"))
        };
        resetRequest.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        var reset = await resetClient.SendAsync(resetRequest);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await signedInClient.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await LoginAsync(resetClient, "reset@orisync.test", OriginalPassword)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(resetClient, "reset@orisync.test", "replacement-password")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeRevokesCurrentSession()
    {
        using var client = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, "change@orisync.test", OriginalPassword)).StatusCode);

        var csrf = await GetCsrfAsync(client);
        using var changeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(
                new ChangePasswordRequest(OriginalPassword, "changed-password"))
        };
        changeRequest.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        var changed = await client.SendAsync(changeRequest);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, "change@orisync.test", "changed-password")).StatusCode);
    }

    [Fact]
    public async Task InactiveSessionExpiresAfterThirtyMinutes()
    {
        using var client = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, "expiry@orisync.test", OriginalPassword)).StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var cutoff = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(31));
            await dbContext.Sessions
                .Where(session => session.RevokedAt == null &&
                    session.Account.Person.Emails.Any(email =>
                        email.NormalizedEmail == "expiry@orisync.test"))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.CreatedAt, cutoff.Subtract(TimeSpan.FromMinutes(1)))
                    .SetProperty(session => session.LastSeenAt, cutoff));
        }

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task AdminRecoveryRotatesCodeAndReplacesPassword()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        using var recoveryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/recover-admin")
        {
            Content = JsonContent.Create(new RecoverAdminRequest(
                "admin@orisync.test",
                AdminRecoveryCode,
                "recovered-password"))
        };
        recoveryRequest.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);

        var recovery = await client.SendAsync(recoveryRequest);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var payload = await recovery.Content.ReadFromJsonAsync<AdminRecoveryResponse>();
        Assert.NotNull(payload);
        Assert.NotEqual(AdminRecoveryCode, payload.RecoveryCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(client, "admin@orisync.test", "recovered-password")).StatusCode);

        using var reusedCodeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/recover-admin")
        {
            Content = JsonContent.Create(new RecoverAdminRequest(
                "admin@orisync.test",
                AdminRecoveryCode,
                "another-password"))
        };
        reusedCodeRequest.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.SendAsync(reusedCodeRequest)).StatusCode);
    }

    private HttpClient CreateClient() => _factory!.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        return payload!.RequestToken;
    }

    private static async Task SeedMentorAsync(
        OriSyncDbContext dbContext,
        string mentorNumber,
        string loginEmail)
    {
        var now = DateTimeOffset.UtcNow;
        var person = new Person
        {
            PersonType = PersonType.Mentor,
            InstitutionNumber = mentorNumber,
            FirstName = "Test",
            Surname = "Mentor",
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = loginEmail,
            NormalizedEmail = loginEmail,
            CreatedAt = now
        });

        var account = new Account
        {
            Person = person,
            Role = AccountRole.Mentor,
            CreatedAt = now,
            IsActive = true,
            MustChangePassword = true
        };
        account.PasswordHash = new PasswordHasher<Account>()
            .HashPassword(account, OriginalPassword);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(OriSyncDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var person = new Person
        {
            PersonType = PersonType.Admin,
            FirstName = "Admin",
            Surname = "User",
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = "admin@orisync.test",
            NormalizedEmail = "admin@orisync.test",
            CreatedAt = now
        });

        var account = new Account
        {
            Person = person,
            Role = AccountRole.Admin,
            CreatedAt = now,
            IsActive = true,
            MustChangePassword = false,
            RecoveryCodeHash = CredentialSecrets.Hash(AdminRecoveryCode),
            RecoveryCodeIssuedAt = now
        };
        account.PasswordHash = new PasswordHasher<Account>()
            .HashPassword(account, OriginalPassword);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
    }
}
