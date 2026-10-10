using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;
using OriSync.Api.MentorManagement;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class MentorManagementEndpointsTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@orisync.test";
    private const string AdminPassword = "test-password";
    private const string MentorPassword = "Secure!Password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_mentor_management_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly RecordingInvitationSender _sender = new();
    private WebApplicationFactory<Program>? _factory;
    private long _groupId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString());
            builder.UseSetting("PasswordReset:DispatcherEnabled", "false");
            builder.UseSetting("MentorInvitations:DispatcherEnabled", "false");
            builder.UseSetting("MentorInvitations:PublicBaseUrl", "https://orisync.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMentorInvitationEmailSender>();
                services.AddSingleton<IMentorInvitationEmailSender>(_sender);
            });
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedAdminAsync(dbContext);
        _groupId = await SeedActiveGroupAsync(dbContext);
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
    public async Task DirectoryAndInvitationAdministrationRequireAnAdmin()
    {
        using var anonymous = CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/mentors")).StatusCode);

        using var admin = await CreateAdminClientAsync();
        var invitation = await CreateInvitationAsync(admin, "new.mentor@orisync.test");
        Assert.Equal("Pending delivery", invitation.Status);
        Assert.Equal(_groupId, invitation.GroupId);

        var duplicate = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            "/api/admin/mentor-invitations",
            new CreateMentorInvitationRequest("new.mentor@orisync.test", _groupId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var directory = await admin.GetFromJsonAsync<MentorDirectoryResponse>(
            "/api/admin/mentors?page=1&pageSize=20&status=All");
        Assert.NotNull(directory);
        Assert.Equal(20, directory.PageSize);
        Assert.Contains(directory.Items, item =>
            item.RecordType == "Invitation" && item.Email == "new.mentor@orisync.test");
    }

    [Fact]
    public async Task DeliveredInvitationIsSingleUseAndCreatesAUsableMentorAccount()
    {
        using var admin = await CreateAdminClientAsync();
        var invitation = await CreateInvitationAsync(admin, "mentor.activation@orisync.test");
        await ProcessInvitationEmailAsync();

        var (invitationId, token) = ParseActivationLink(Assert.Single(_sender.ActivationLinks));
        Assert.Equal(invitation.Id, invitationId);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var stored = await dbContext.MentorInvitations.SingleAsync(item => item.Id == invitation.Id);
            Assert.NotNull(stored.SentAt);
            Assert.Equal(stored.SentAt!.Value.AddHours(24), stored.ExpiresAt);
            Assert.Null(stored.ProtectedToken);
            Assert.NotEqual(token, stored.TokenHash);
        }

        using var anonymous = CreateClient();
        var validated = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/mentor-activation/validate",
            new ValidateMentorInvitationRequest(invitationId, token));
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);

        var weakPassword = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/mentor-activation/accept",
            new AcceptMentorInvitationRequest(
                invitationId,
                token,
                "Jane",
                "Mentor",
                "223456789",
                "lowercase"));
        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);

        var accepted = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/mentor-activation/accept",
            new AcceptMentorInvitationRequest(
                invitationId,
                token,
                "Jane",
                "Mentor",
                "223456789",
                MentorPassword));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var reused = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/mentor-activation/validate",
            new ValidateMentorInvitationRequest(invitationId, token));
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);

        var login = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest("mentor.activation@orisync.test", MentorPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResendInvalidatesOldLinkAndDisablingMentorRevokesTheirSession()
    {
        using var admin = await CreateAdminClientAsync();
        var invitation = await CreateInvitationAsync(admin, "replacement@orisync.test");
        await ProcessInvitationEmailAsync();
        var oldLink = ParseActivationLink(Assert.Single(_sender.ActivationLinks));

        var resend = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/admin/mentor-invitations/{invitation.Id}/resend");
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        await ProcessInvitationEmailAsync();
        var newLink = ParseActivationLink(_sender.ActivationLinks.Last());
        Assert.NotEqual(oldLink.Token, newLink.Token);

        using var anonymous = CreateClient();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendJsonAsync(
                anonymous,
                HttpMethod.Post,
                "/api/auth/mentor-activation/validate",
                new ValidateMentorInvitationRequest(oldLink.InvitationId, oldLink.Token))).StatusCode);

        var accepted = await SendJsonAsync(
            anonymous,
            HttpMethod.Post,
            "/api/auth/mentor-activation/accept",
            new AcceptMentorInvitationRequest(
                newLink.InvitationId,
                newLink.Token,
                "John",
                "Mentor",
                "223456788",
                MentorPassword));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        using var mentor = CreateClient();
        (await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest("replacement@orisync.test", MentorPassword))).EnsureSuccessStatusCode();
        var directory = await admin.GetFromJsonAsync<MentorDirectoryResponse>(
            "/api/admin/mentors?page=1&pageSize=20&status=Active");
        var account = Assert.Single(directory!.Items, item => item.Email == "replacement@orisync.test");

        var disabled = await SendJsonAsync(
            admin,
            HttpMethod.Post,
            $"/api/admin/mentors/{account.Id}/disable");
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mentor.GetAsync("/api/auth/session")).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var stored = await dbContext.Accounts.SingleAsync(item => item.Id == account.Id);
        Assert.False(stored.IsActive);
        Assert.Equal(_groupId, stored.CurrentGroupId);
        Assert.NotNull(stored.DeletionEligibleAt);
    }

    [Fact]
    public async Task DeactivatedInvitationGroupMustBeReassignedBeforeActivation()
    {
        using var admin = await CreateAdminClientAsync();
        var invitation = await CreateInvitationAsync(admin, "reassigned@orisync.test");
        await ProcessInvitationEmailAsync();
        var link = ParseActivationLink(Assert.Single(_sender.ActivationLinks));
        long blueGroupId;

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var red = await dbContext.Groups.SingleAsync(item => item.Id == _groupId);
            red.IsActive = false;
            red.DeactivatedAt = DateTimeOffset.UtcNow;
            var blue = new OrientationGroup
            {
                OrientationId = red.OrientationId,
                Name = "BLUE",
                NormalizedName = "blue",
                BadgeColor = "#1565C0",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.Groups.Add(blue);
            await dbContext.SaveChangesAsync();
            blueGroupId = blue.Id;
        }

        using var anonymous = CreateClient();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendJsonAsync(
                anonymous,
                HttpMethod.Post,
                "/api/auth/mentor-activation/validate",
                new ValidateMentorInvitationRequest(link.InvitationId, link.Token))).StatusCode);

        var reassigned = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/admin/mentor-invitations/{invitation.Id}/group",
            new ChangeMentorInvitationGroupRequest(blueGroupId));
        Assert.Equal(HttpStatusCode.OK, reassigned.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendJsonAsync(
                anonymous,
                HttpMethod.Post,
                "/api/auth/mentor-activation/validate",
                new ValidateMentorInvitationRequest(link.InvitationId, link.Token))).StatusCode);
    }

    private async Task ProcessInvitationEmailAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<MentorInvitationEmailProcessor>();
        Assert.True(await processor.ProcessOneAsync(CancellationToken.None));
        Assert.False(await processor.ProcessOneAsync(CancellationToken.None));
    }

    private HttpClient CreateClient() => _factory!.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        (await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest(AdminEmail, AdminPassword))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<MentorDirectoryItemResponse> CreateInvitationAsync(
        HttpClient client,
        string email)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/admin/mentor-invitations",
            new CreateMentorInvitationRequest(email, _groupId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MentorDirectoryItemResponse>())!;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf.RequestToken);
        return await client.SendAsync(request);
    }

    private static (long InvitationId, string Token) ParseActivationLink(string link)
    {
        var uri = new Uri(link);
        var values = uri.Fragment.TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        return (long.Parse(values["invitation"], System.Globalization.CultureInfo.InvariantCulture), values["token"]);
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
            Email = AdminEmail,
            NormalizedEmail = AdminEmail,
            CreatedAt = now
        });
        var account = new Account
        {
            Person = person,
            Role = AccountRole.Admin,
            IsActive = true,
            CreatedAt = now
        };
        account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, AdminPassword);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<long> SeedActiveGroupAsync(OriSyncDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var orientation = new Orientation
        {
            Year = 2027,
            Name = "First Year Orientation 2027",
            StartDate = new DateOnly(2027, 2, 1),
            EndDate = new DateOnly(2027, 2, 12),
            AttendanceOpensAt = new TimeOnly(7, 45),
            AttendanceClosesAt = new TimeOnly(15, 30),
            IsActive = true,
            RetentionDueAt = now.AddMonths(6),
            CreatedAt = now
        };
        var group = new OrientationGroup
        {
            Orientation = orientation,
            Name = "RED",
            NormalizedName = "red",
            BadgeColor = "#C62828",
            IsActive = true,
            CreatedAt = now
        };
        dbContext.Groups.Add(group);
        await dbContext.SaveChangesAsync();
        return group.Id;
    }

    private sealed class RecordingInvitationSender : IMentorInvitationEmailSender
    {
        public List<string> ActivationLinks { get; } = [];

        public Task<bool> SendAsync(
            string recipient,
            string activationLink,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            ActivationLinks.Add(activationLink);
            return Task.FromResult(true);
        }
    }
}
