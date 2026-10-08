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
using OriSync.Api.OrientationManagement;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class OrientationManagementEndpointsTests : IAsyncLifetime
{
    private const string Password = "test-password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_orientation_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private long _mentorAccountId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString());
            builder.UseSetting("PasswordReset:DispatcherEnabled", "false");
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedAccountAsync(
            dbContext,
            PersonType.Admin,
            AccountRole.Admin,
            null,
            "admin@orisync.test");
        _mentorAccountId = await SeedAccountAsync(
            dbContext,
            PersonType.Mentor,
            AccountRole.Mentor,
            "223456781",
            "mentor@orisync.test");
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
    public async Task OrientationSettingsRequireAnAdminSession()
    {
        using var anonymousClient = CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/admin/orientations")).StatusCode);

        using var mentorClient = CreateClient();
        await LoginAsync(mentorClient, "mentor@orisync.test");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await mentorClient.GetAsync("/api/admin/orientations")).StatusCode);

        using var adminClient = CreateClient();
        await LoginAsync(adminClient, "admin@orisync.test");
        Assert.Equal(
            HttpStatusCode.OK,
            (await adminClient.GetAsync("/api/admin/orientations")).StatusCode);
    }

    [Fact]
    public async Task CreateOrientationGeneratesNameWeekdayCalendarAndRetentionDate()
    {
        using var client = await CreateAdminClientAsync();

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/admin/orientations",
            OrientationRequest(2027));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orientation = await response.Content.ReadFromJsonAsync<OrientationResponse>();
        Assert.NotNull(orientation);
        Assert.Equal("First Year Orientation 2027", orientation.Name);
        Assert.False(orientation.IsActive);
        Assert.Equal(
            [
                new DateOnly(2027, 2, 1),
                new DateOnly(2027, 2, 2),
                new DateOnly(2027, 2, 3),
                new DateOnly(2027, 2, 4),
                new DateOnly(2027, 2, 5)
            ],
            orientation.OperatingDates);
        Assert.Equal(
            new DateTimeOffset(2027, 8, 6, 22, 0, 0, TimeSpan.Zero),
            orientation.RetentionDueAt);
    }

    [Fact]
    public async Task ActivationRequiresAGroupAndAtomicallyReplacesTheActiveOrientation()
    {
        using var client = await CreateAdminClientAsync();
        var first = await CreateOrientationAsync(client, 2027);

        var missingGroup = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{first.Id}/activate");
        Assert.Equal(HttpStatusCode.Conflict, missingGroup.StatusCode);

        var firstWithGroup = await CreateGroupAsync(client, first.Id, "red", null);
        Assert.Equal("RED", Assert.Single(firstWithGroup.Groups).Name);
        Assert.Equal("#C62828", Assert.Single(firstWithGroup.Groups).BadgeColor);

        var activatedFirst = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{first.Id}/activate");
        Assert.Equal(HttpStatusCode.OK, activatedFirst.StatusCode);

        var second = await CreateOrientationAsync(client, 2028);
        await CreateGroupAsync(client, second.Id, "BLUE", null);
        var activatedSecond = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{second.Id}/activate");
        Assert.Equal(HttpStatusCode.OK, activatedSecond.StatusCode);

        var orientations = await client.GetFromJsonAsync<List<OrientationResponse>>(
            "/api/admin/orientations");
        Assert.NotNull(orientations);
        Assert.False(orientations.Single(item => item.Id == first.Id).IsActive);
        Assert.True(orientations.Single(item => item.Id == second.Id).IsActive);

        var deactivated = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{second.Id}/deactivate");
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        orientations = await client.GetFromJsonAsync<List<OrientationResponse>>(
            "/api/admin/orientations");
        Assert.NotNull(orientations);
        Assert.DoesNotContain(orientations, item => item.IsActive);
    }

    [Fact]
    public async Task CustomGroupsRequireAColourAndUnusedGroupsCanBeDeleted()
    {
        using var client = await CreateAdminClientAsync();
        var orientation = await CreateOrientationAsync(client, 2027);

        var missingColour = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{orientation.Id}/groups",
            new SaveOrientationGroupRequest("TEAM A", null));
        Assert.Equal(HttpStatusCode.BadRequest, missingColour.StatusCode);

        var withGroup = await CreateGroupAsync(client, orientation.Id, "Team A", "#123ABC");
        var group = Assert.Single(withGroup.Groups);
        Assert.Equal("TEAM A", group.Name);
        Assert.True(group.CanDelete);

        var duplicate = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{orientation.Id}/groups",
            new SaveOrientationGroupRequest("team a", "#654321"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var deleted = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/api/admin/orientations/{orientation.Id}/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task UsedGroupNameDeletionAndDeactivationAreProtected()
    {
        using var client = await CreateAdminClientAsync();
        var orientation = await CreateOrientationAsync(client, 2027);
        var withGroup = await CreateGroupAsync(client, orientation.Id, "RED", null);
        var group = Assert.Single(withGroup.Groups);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var mentor = await dbContext.Accounts.SingleAsync(item => item.Id == _mentorAccountId);
            mentor.CurrentGroupId = group.Id;
            await dbContext.SaveChangesAsync();
        }

        var renamed = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/admin/orientations/{orientation.Id}/groups/{group.Id}",
            new SaveOrientationGroupRequest("CRIMSON", "#A00000"));
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        var recoloured = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/admin/orientations/{orientation.Id}/groups/{group.Id}",
            new SaveOrientationGroupRequest("RED", "#A00000"));
        Assert.Equal(HttpStatusCode.OK, recoloured.StatusCode);

        var deactivated = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{orientation.Id}/groups/{group.Id}/deactivate");
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);

        var deleted = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/api/admin/orientations/{orientation.Id}/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
    }

    [Fact]
    public async Task AttendanceLocksHistoricalSettingsButAllowsFutureClosingTimeCorrections()
    {
        using var client = await CreateAdminClientAsync();
        var orientation = await CreateOrientationAsync(client, 2199);
        var withGroup = await CreateGroupAsync(client, orientation.Id, "RED", null);
        var group = Assert.Single(withGroup.Groups);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var student = new Person
            {
                PersonType = PersonType.Student,
                InstitutionNumber = "223456782",
                FirstName = "Jane",
                Surname = "Doe",
                Initials = "JD",
                AgeGroup = StudentAgeGroup.C2,
                Gender = Gender.Female,
                Ethnicity = Ethnicity.African,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var enrollment = new StudentEnrollment
            {
                OrientationId = orientation.Id,
                Student = student,
                CurrentGroupId = group.Id,
                EnrolledAt = DateTimeOffset.UtcNow
            };
            enrollment.AttendanceRecords.Add(new AttendanceRecord
            {
                OrientationId = orientation.Id,
                GroupId = group.Id,
                AttendanceDate = new DateOnly(2199, 2, 2),
                Status = AttendanceStatus.Present,
                CaptureMethod = AttendanceCaptureMethod.Registration
            });
            dbContext.StudentEnrollments.Add(enrollment);
            await dbContext.SaveChangesAsync();
        }

        var changedStartDate = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/admin/orientations/{orientation.Id}",
            OrientationRequest(2199) with { StartDate = new DateOnly(2199, 2, 2) });
        Assert.Equal(HttpStatusCode.Conflict, changedStartDate.StatusCode);

        var correctedClosingTime = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/api/admin/orientations/{orientation.Id}",
            OrientationRequest(2199) with { AttendanceClosesAt = new TimeOnly(16, 0) });
        Assert.Equal(HttpStatusCode.OK, correctedClosingTime.StatusCode);
        var updated = await correctedClosingTime.Content.ReadFromJsonAsync<OrientationResponse>();
        Assert.NotNull(updated);
        Assert.Equal(new TimeOnly(16, 0), updated.AttendanceClosesAt);
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
        await LoginAsync(client, "admin@orisync.test");
        return client;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var csrfResponse = await client.GetFromJsonAsync<CsrfTokenResponse>("/api/auth/csrf");
        Assert.NotNull(csrfResponse);
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrfResponse.RequestToken);
        return await client.SendAsync(request);
    }

    private static SaveOrientationRequest OrientationRequest(int year) => new(
        year,
        null,
        new DateOnly(year, 2, 1),
        new DateOnly(year, 2, 7),
        new TimeOnly(7, 45),
        new TimeOnly(15, 30));

    private static async Task<OrientationResponse> CreateOrientationAsync(
        HttpClient client,
        int year)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/admin/orientations",
            OrientationRequest(year));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrientationResponse>())!;
    }

    private static async Task<OrientationResponse> CreateGroupAsync(
        HttpClient client,
        long orientationId,
        string name,
        string? badgeColor)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/admin/orientations/{orientationId}/groups",
            new SaveOrientationGroupRequest(name, badgeColor));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrientationResponse>())!;
    }

    private static async Task<long> SeedAccountAsync(
        OriSyncDbContext dbContext,
        PersonType personType,
        AccountRole role,
        string? institutionNumber,
        string email)
    {
        var person = new Person
        {
            PersonType = personType,
            InstitutionNumber = institutionNumber,
            FirstName = "Test",
            Surname = role.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = email,
            NormalizedEmail = email,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var account = new Account
        {
            Person = person,
            Role = role,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, Password);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
