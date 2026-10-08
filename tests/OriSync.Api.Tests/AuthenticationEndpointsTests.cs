using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
    private readonly RecordingPasswordResetEmailSender _emailSender = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString());
            builder.UseSetting("PasswordReset:CodeLength", "6");
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IPasswordResetEmailSender>(_emailSender));
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedMentorAsync(dbContext, "223450001", "login@orisync.test");
        await SeedMentorAsync(dbContext, "223450002", "reset@orisync.test");
        await SeedMentorAsync(dbContext, "223450003", "change@orisync.test");
        await SeedMentorAsync(dbContext, "223450004", "expiry@orisync.test");
        await SeedMentorAsync(dbContext, "223450005", "cooldown@orisync.test");
        await SeedMentorAsync(dbContext, "223450006", "otp-expiry@orisync.test");
        await SeedMentorAsync(dbContext, "223450007", "attempts@orisync.test");
        await SeedMentorAsync(dbContext, "223450008", "hourly-limit@orisync.test");
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
    public async Task MentorEmailOtpResetRevokesSessionAndReplacesPassword()
    {
        using var signedInClient = CreateClient();
        using var resetClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(signedInClient, "reset@orisync.test", OriginalPassword)).StatusCode);

        var csrf = await GetCsrfAsync(resetClient);
        using var requestCode = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/mentor-password-reset/request")
        {
            Content = JsonContent.Create(
                new RequestMentorPasswordResetRequest("223450002", "reset@orisync.test"))
        };
        requestCode.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        var requested = await resetClient.SendAsync(requestCode);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Equal("reset@orisync.test", _emailSender.LastRecipient);
        Assert.Matches("^[0-9]{6}$", _emailSender.LastCode);

        using var resetRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/mentor-password-reset/complete")
        {
            Content = JsonContent.Create(new CompleteMentorPasswordResetRequest(
                "223450002",
                "reset@orisync.test",
                _emailSender.LastCode!,
                "replacement-password"))
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
    public async Task PasswordResetRequestDoesNotRevealWhetherMentorExists()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/mentor-password-reset/request")
        {
            Content = JsonContent.Create(
                new RequestMentorPasswordResetRequest("223459999", "missing@orisync.test"))
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PasswordResetRequestResponse>();
        Assert.Equal("If the mentor account exists, a password reset code was sent.", payload!.Message);
    }

    [Fact]
    public async Task PasswordResetRequestEnforcesSixtySecondCooldown()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);

        var first = await RequestPasswordResetAsync(
            client,
            csrf,
            "223450005",
            "cooldown@orisync.test");
        var second = await RequestPasswordResetAsync(
            client,
            csrf,
            "223450005",
            "cooldown@orisync.test");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, _emailSender.SendCount);
    }

    [Fact]
    public async Task PasswordResetRequestEnforcesFiveRequestsPerHour()
    {
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var accountId = await dbContext.Accounts
                .Where(account => account.Person.InstitutionNumber == "223450008")
                .Select(account => account.Id)
                .SingleAsync();
            var now = DateTimeOffset.UtcNow;
            for (var index = 1; index <= 5; index++)
            {
                var otp = new PasswordResetOtp
                {
                    AccountId = accountId,
                    RequestedAt = now.Subtract(TimeSpan.FromMinutes(index * 5)),
                    ExpiresAt = now.Subtract(TimeSpan.FromMinutes(index * 5 - 5)),
                    SentAt = now.Subtract(TimeSpan.FromMinutes(index * 5)),
                    ConsumedAt = now.Subtract(TimeSpan.FromMinutes(index * 5 - 1))
                };
                otp.CodeHash = new PasswordHasher<PasswordResetOtp>().HashPassword(
                    otp,
                    index.ToString("D6", CultureInfo.InvariantCulture));
                dbContext.PasswordResetOtps.Add(otp);
            }

            await dbContext.SaveChangesAsync();
        }

        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        var response = await RequestPasswordResetAsync(
            client,
            csrf,
            "223450008",
            "hourly-limit@orisync.test");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, _emailSender.SendCount);
    }

    [Fact]
    public async Task ExpiredPasswordResetCodeIsRejected()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        await RequestPasswordResetAsync(
            client,
            csrf,
            "223450006",
            "otp-expiry@orisync.test");

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var requestedAt = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(6));
            await dbContext.PasswordResetOtps
                .Where(otp => otp.Account.Person.InstitutionNumber == "223450006")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(otp => otp.RequestedAt, requestedAt)
                    .SetProperty(otp => otp.SentAt, requestedAt)
                    .SetProperty(
                        otp => otp.ExpiresAt,
                        DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(1))));
        }

        var response = await CompletePasswordResetAsync(
            client,
            csrf,
            "223450006",
            "otp-expiry@orisync.test",
            _emailSender.LastCode!,
            "replacement-password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PasswordResetCodeIsLockedAfterFiveFailedAttempts()
    {
        using var client = CreateClient();
        var csrf = await GetCsrfAsync(client);
        await RequestPasswordResetAsync(
            client,
            csrf,
            "223450007",
            "attempts@orisync.test");
        var validCode = _emailSender.LastCode!;
        var invalidCode = validCode == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var rejected = await CompletePasswordResetAsync(
                client,
                csrf,
                "223450007",
                "attempts@orisync.test",
                invalidCode,
                "replacement-password");
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        var locked = await CompletePasswordResetAsync(
            client,
            csrf,
            "223450007",
            "attempts@orisync.test",
            validCode,
            "replacement-password");
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
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

    private static async Task<HttpResponseMessage> RequestPasswordResetAsync(
        HttpClient client,
        string csrf,
        string mentorNumber,
        string email)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/mentor-password-reset/request")
        {
            Content = JsonContent.Create(new RequestMentorPasswordResetRequest(mentorNumber, email))
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CompletePasswordResetAsync(
        HttpClient client,
        string csrf,
        string mentorNumber,
        string email,
        string code,
        string newPassword)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/auth/mentor-password-reset/complete")
        {
            Content = JsonContent.Create(new CompleteMentorPasswordResetRequest(
                mentorNumber,
                email,
                code,
                newPassword))
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf);
        return await client.SendAsync(request);
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

    private sealed class RecordingPasswordResetEmailSender : IPasswordResetEmailSender
    {
        public string? LastRecipient { get; private set; }
        public string? LastCode { get; private set; }
        public int SendCount { get; private set; }

        public Task<bool> SendAsync(
            string recipient,
            string firstName,
            string code,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            LastRecipient = recipient;
            LastCode = code;
            SendCount++;
            return Task.FromResult(true);
        }
    }
}
