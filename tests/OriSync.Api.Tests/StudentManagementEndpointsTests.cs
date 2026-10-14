using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OriSync.Api.Attendance;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Dashboards;
using OriSync.Api.Domain;
using OriSync.Api.StudentManagement;
using OriSync.Api.StudentQrCodes;
using OriSync.Api.RegisterReports;
using SkiaSharp;
using Testcontainers.PostgreSql;
using ZXing;
using ZXing.Common;

namespace OriSync.Api.Tests;

public sealed class StudentManagementEndpointsTests : IAsyncLifetime
{
    private const string Password = "Secure!Password";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_student_management_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly MutableTimeProvider _time = new(
        new DateTimeOffset(2027, 2, 1, 6, 0, 0, TimeSpan.Zero));
    private WebApplicationFactory<Program>? _factory;
    private long _redGroupId;
    private long _blueGroupId;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString());
            builder.UseSetting("PasswordReset:DispatcherEnabled", "false");
            builder.UseSetting("MentorInvitations:DispatcherEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
            });
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
        (_redGroupId, _blueGroupId) = await SeedOrientationAndMentorsAsync(dbContext);
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
    public async Task RegistrationCreatesPresentStudentAndNormalizesUnicodeNames()
    {
        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");
        var response = await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "  Élodie   Anne-Marie  ", "O’Connor"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("RegisteredPresent", created.Outcome);
        Assert.Equal("Élodie Anne-Marie", created.Student.FullNames);
        Assert.Equal("ÉA", created.Student.Initials);
        Assert.Equal("Present", created.Student.AttendanceStatus);
        Assert.Equal("RED", created.Student.GroupName);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.AttendanceRecords)
            .Include(item => item.Student)
            .SingleAsync();
        Assert.Equal("ÉA", enrollment.Student.Initials);
        Assert.Equal(AttendanceStatus.Present, Assert.Single(enrollment.AttendanceRecords).Status);
        Assert.Contains(await dbContext.AuditEvents.ToListAsync(), item =>
            item.EventType == "StudentCreated" &&
            item.EntityId == enrollment.Id &&
            item.ExpiresAt == item.OccurredAt.AddDays(14));
    }

    [Fact]
    public async Task RegistrationRejectsInvalidNamesAndClosedHours()
    {
        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");
        var invalid = await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane3", "Doe"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        _time.SetUtcNow(new DateTimeOffset(2027, 2, 1, 13, 31, 0, TimeSpan.Zero));
        using var closedHoursMentor = await CreateMentorClientAsync("red.mentor@orisync.test");
        var closed = await SendJsonAsync(
            closedHoursMentor,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane", "Doe"));
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
    }

    [Fact]
    public async Task RegistrationRejectsStudentEmailThatDoesNotMatchStudentNumber()
    {
        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");
        var request = Student("223456789", "Jane", "Doe") with
        {
            StudentEmail = "incorrect@tut4life.ac.za"
        };

        var response = await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/mentor/students",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "Student email must be 223456789@tut4life.ac.za.",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardsUseDailyRegisterCountsAndRankCurrentGroups()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        (await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane", "Doe"))).EnsureSuccessStatusCode();
        await AddStudentWithoutAttendanceAsync(_blueGroupId, "223456788", "John", "Smith");

        using var admin = await CreateAdminClientAsync();
        var adminDashboard = await admin.GetFromJsonAsync<AdminDashboardResponse>(
            "/api/admin/dashboard",
            JsonOptions);

        Assert.NotNull(adminDashboard);
        Assert.True(adminDashboard.HasActiveOrientation);
        Assert.Equal(2, adminDashboard.Metrics.TotalStudents);
        Assert.Equal(1, adminDashboard.Metrics.PresentToday);
        Assert.Equal(1, adminDashboard.Metrics.AbsentToday);
        Assert.Equal(50m, adminDashboard.Metrics.AttendanceRate);
        Assert.Equal(["BLUE", "RED"], adminDashboard.GroupRanking.Select(item => item.GroupName));
        Assert.Equal([1, 2], adminDashboard.GroupRanking.Select(item => item.Rank));

        var mentorDashboard = await red.GetFromJsonAsync<MentorDashboardResponse>(
            "/api/mentor/dashboard",
            JsonOptions);
        Assert.NotNull(mentorDashboard);
        Assert.Equal("RED", mentorDashboard.GroupName);
        Assert.Equal(1, mentorDashboard.Metrics.TotalStudents);
        Assert.Equal(1, mentorDashboard.Metrics.PresentToday);
        Assert.Equal(0, mentorDashboard.Metrics.AbsentToday);
        Assert.Equal(100m, mentorDashboard.Metrics.AttendanceRate);
    }

    [Fact]
    public async Task StudentDirectoriesFilterByTodaysAttendanceStatus()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        (await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane", "Doe"))).EnsureSuccessStatusCode();
        await AddStudentWithoutAttendanceAsync(_redGroupId, "223456788", "John", "Smith");

        var present = await red.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/mentor/students?attendanceStatus=Present&page=1&pageSize=10",
            JsonOptions);
        var absent = await red.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/mentor/students?attendanceStatus=Absent&page=1&pageSize=10",
            JsonOptions);

        Assert.NotNull(present);
        Assert.Equal("223456789", Assert.Single(present.Items).StudentNumber);
        Assert.NotNull(absent);
        Assert.Equal("223456788", Assert.Single(absent.Items).StudentNumber);

        using var admin = await CreateAdminClientAsync();
        var adminAbsent = await admin.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/admin/students?groupId=" + _redGroupId +
            "&attendanceStatus=Absent&page=1&pageSize=10",
            JsonOptions);
        Assert.NotNull(adminAbsent);
        Assert.Equal("223456788", Assert.Single(adminAbsent.Items).StudentNumber);
    }

    [Fact]
    public async Task MentorDownloadsFilteredCurrentGroupRegisterWithoutStoringIt()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        (await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane Mary", "Doe"))).EnsureSuccessStatusCode();
        await AddStudentWithoutAttendanceAsync(_redGroupId, "223456788", "John", "Smith");

        var summary = await red.GetFromJsonAsync<RegisterSummaryResponse>(
            "/api/mentor/registers/today/summary?attendanceStatus=Present",
            JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.RowCount);
        Assert.Equal(RegisterFilter.Present, summary.AttendanceStatus);

        var response = await red.GetAsync(
            "/api/mentor/registers/today?attendanceStatus=Present");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "Red-2027-02-01-08h00-Present.pdf",
            response.Content.Headers.ContentDisposition?.FileNameStar ??
                response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.True(content.AsSpan().StartsWith("%PDF"u8));

        var absent = await red.GetFromJsonAsync<RegisterSummaryResponse>(
            "/api/mentor/registers/today/summary?attendanceStatus=Absent",
            JsonOptions);
        Assert.NotNull(absent);
        Assert.Equal(1, absent.RowCount);
    }

    [Fact]
    public async Task AdminDownloadsOneZipOfMatchingGroupPdfsAndRejectsEmptyOrFutureRegisters()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        (await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane", "Doe"))).EnsureSuccessStatusCode();
        await AddStudentWithoutAttendanceAsync(_blueGroupId, "223456788", "John", "Smith");

        using var admin = await CreateAdminClientAsync();
        var summary = await admin.GetFromJsonAsync<RegisterSummaryResponse>(
            $"/api/admin/registers/summary?orientationId=1&date=2027-02-01&attendanceStatus=All",
            JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(2, summary.RowCount);
        Assert.Equal(2, summary.GroupCount);

        var groupSummaries = await admin.GetFromJsonAsync<RegisterGroupSummaryResponse[]>(
            "/api/admin/registers/groups?orientationId=1&date=2027-02-01&attendanceStatus=Present",
            JsonOptions);
        Assert.NotNull(groupSummaries);
        Assert.Collection(
            groupSummaries,
            blue =>
            {
                Assert.Equal("BLUE", blue.GroupName);
                Assert.Equal(1, blue.TotalStudents);
                Assert.Equal(0, blue.PresentStudents);
                Assert.Equal(1, blue.AbsentStudents);
                Assert.Equal(0, blue.MatchingStudents);
            },
            redGroup =>
            {
                Assert.Equal("RED", redGroup.GroupName);
                Assert.Equal(1, redGroup.TotalStudents);
                Assert.Equal(1, redGroup.PresentStudents);
                Assert.Equal(0, redGroup.AbsentStudents);
                Assert.Equal(1, redGroup.MatchingStudents);
            });

        var response = await admin.GetAsync(
            "/api/admin/registers/zip?orientationId=1&date=2027-02-01&attendanceStatus=All");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "OriSync-2027-02-01-08h00-All.zip",
            response.Content.Headers.ContentDisposition?.FileNameStar ??
                response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        await using var zipStream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.Equal(["Blue-2027-02-01-08h00-All.pdf", "Red-2027-02-01-08h00-All.pdf"],
            archive.Entries.Select(item => item.FullName));
        foreach (var entry in archive.Entries)
        {
            await using var entryStream = entry.Open();
            var signature = new byte[4];
            await entryStream.ReadExactlyAsync(signature);
            Assert.Equal("%PDF"u8.ToArray(), signature);
        }

        var empty = await admin.GetAsync(
            $"/api/admin/registers/pdf?orientationId=1&date=2027-02-01&" +
            $"groupId={_blueGroupId}&attendanceStatus=Present");
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        var future = await admin.GetAsync(
            "/api/admin/registers/zip?orientationId=1&date=2027-02-02&attendanceStatus=All");
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");
        var forbidden = await mentor.GetAsync(
            "/api/admin/registers/zip?orientationId=1&date=2027-02-01&attendanceStatus=All");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task DirectoryMaterializesTodaysDefaultAbsentRow()
    {
        var enrollmentId = await AddStudentWithoutAttendanceAsync(
            _redGroupId,
            "223456789",
            "Jane",
            "Doe");
        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");

        var directory = await mentor.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/mentor/students?page=1&pageSize=10",
            JsonOptions);

        Assert.NotNull(directory);
        Assert.Equal("Absent", Assert.Single(directory.Items).AttendanceStatus);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var attendance = await dbContext.AttendanceRecords.SingleAsync(
            item => item.StudentEnrollmentId == enrollmentId);
        Assert.Equal(AttendanceStatus.Absent, attendance.Status);
        Assert.Equal(AttendanceCaptureMethod.Automatic, attendance.CaptureMethod);
        Assert.Equal(_redGroupId, attendance.GroupId);
        Assert.Null(attendance.MarkedByAccountId);
    }

    [Fact]
    public async Task QrScanMarksPresentAndThenReturnsDuplicateWarning()
    {
        var enrollmentId = await AddStudentWithoutAttendanceAsync(
            _redGroupId,
            "223456789",
            "Jane Mary",
            "Doe");
        var qrUrl = CreateQrUrl("223456789", "Jane Mary", "Doe", 1);
        using var mentor = await CreateMentorClientAsync("red.mentor@orisync.test");

        var markedResponse = await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/mentor/attendance/scan",
            new ScanAttendanceRequest(qrUrl));
        markedResponse.EnsureSuccessStatusCode();
        var marked = await markedResponse.Content.ReadFromJsonAsync<AttendanceMutationResponse>(JsonOptions);
        Assert.NotNull(marked);
        Assert.Equal("MarkedPresent", marked.Outcome);
        Assert.Equal(AttendanceStatus.Present, marked.Status);
        Assert.Equal("Jane Mary Doe was marked Present.", marked.Message);

        var duplicateResponse = await SendJsonAsync(
            mentor,
            HttpMethod.Post,
            "/api/mentor/attendance/scan",
            new ScanAttendanceRequest(qrUrl));
        duplicateResponse.EnsureSuccessStatusCode();
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<AttendanceMutationResponse>(JsonOptions);
        Assert.NotNull(duplicate);
        Assert.Equal("AlreadyPresent", duplicate.Outcome);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var attendance = await dbContext.AttendanceRecords.SingleAsync(
            item => item.StudentEnrollmentId == enrollmentId);
        Assert.Equal(AttendanceStatus.Present, attendance.Status);
        Assert.Equal(AttendanceCaptureMethod.Qr, attendance.CaptureMethod);
        Assert.NotNull(attendance.MarkedByAccountId);
    }

    [Fact]
    public async Task QrScanRejectsExpiredInvalidWrongGroupAndClosedAttendance()
    {
        await AddStudentWithoutAttendanceAsync(_redGroupId, "223456789", "Jane", "Doe", qrVersion: 2);
        await AddStudentWithoutAttendanceAsync(_blueGroupId, "223456788", "John", "Smith");
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");

        await AssertAttendanceProblemAsync(
            await SendJsonAsync(
                red,
                HttpMethod.Post,
                "/api/mentor/attendance/scan",
                new ScanAttendanceRequest(CreateQrUrl("223456789", "Jane", "Doe", 1))),
            HttpStatusCode.Conflict,
            "ExpiredQr");
        await AssertAttendanceProblemAsync(
            await SendJsonAsync(
                red,
                HttpMethod.Post,
                "/api/mentor/attendance/scan",
                new ScanAttendanceRequest("https://example.test/not-an-orisync-code")),
            HttpStatusCode.BadRequest,
            "InvalidQr");
        await AssertAttendanceProblemAsync(
            await SendJsonAsync(
                red,
                HttpMethod.Post,
                "/api/mentor/attendance/scan",
                new ScanAttendanceRequest(CreateQrUrl("223456788", "John", "Smith", 1))),
            HttpStatusCode.Conflict,
            "WrongGroup");

        _time.SetUtcNow(new DateTimeOffset(2027, 2, 1, 13, 31, 0, TimeSpan.Zero));
        using var afterHours = await CreateMentorClientAsync("red.mentor@orisync.test");
        await AssertAttendanceProblemAsync(
            await SendJsonAsync(
                afterHours,
                HttpMethod.Post,
                "/api/mentor/attendance/scan",
                new ScanAttendanceRequest(CreateQrUrl("223456789", "Jane", "Doe", 2))),
            HttpStatusCode.Conflict,
            "AttendanceClosed");
    }

    [Fact]
    public async Task ManualCorrectionUsesLastSubmissionAndEnforcesMentorGroup()
    {
        var enrollmentId = await AddStudentWithoutAttendanceAsync(
            _redGroupId,
            "223456789",
            "Jane",
            "Doe");
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");

        (await SendJsonAsync(
            red,
            HttpMethod.Put,
            $"/api/mentor/attendance/{enrollmentId}",
            new CorrectAttendanceRequest(AttendanceStatus.Present))).EnsureSuccessStatusCode();
        var absentResponse = await SendJsonAsync(
            red,
            HttpMethod.Put,
            $"/api/mentor/attendance/{enrollmentId}",
            new CorrectAttendanceRequest(AttendanceStatus.Absent));
        absentResponse.EnsureSuccessStatusCode();
        var absent = await absentResponse.Content.ReadFromJsonAsync<AttendanceMutationResponse>(JsonOptions);
        Assert.Equal(AttendanceStatus.Absent, absent!.Status);

        using var blue = await CreateMentorClientAsync("blue.mentor@orisync.test");
        var forbidden = await SendJsonAsync(
            blue,
            HttpMethod.Put,
            $"/api/mentor/attendance/{enrollmentId}",
            new CorrectAttendanceRequest(AttendanceStatus.Present));
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var attendance = await dbContext.AttendanceRecords.SingleAsync(
            item => item.StudentEnrollmentId == enrollmentId);
        Assert.Equal(AttendanceStatus.Absent, attendance.Status);
        Assert.Equal(AttendanceCaptureMethod.Manual, attendance.CaptureMethod);
    }

    [Fact]
    public async Task RemovalThenDifferentGroupOnboardingPreservesTodayAndMovesTomorrow()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        var createdResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane Mary", "Doe"));
        var created = (await createdResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendJsonAsync(
                red,
                HttpMethod.Post,
                $"/api/mentor/students/{created.Student.EnrollmentId}/remove")).StatusCode);

        using var blue = await CreateMentorClientAsync("blue.mentor@orisync.test");
        var movedResponse = await SendJsonAsync(
            blue,
            HttpMethod.Post,
            "/api/mentor/students/onboard",
            new OnboardExistingStudentRequest("223456789"));
        movedResponse.EnsureSuccessStatusCode();
        var moved = (await movedResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        Assert.Equal("MovesTomorrow", moved.Outcome);
        Assert.Equal("BLUE", moved.Student.GroupName);
        Assert.Equal("—", moved.Student.AttendanceStatus);
        Assert.True(moved.Student.IsIncomingTransfer);

        var blockedEdit = await SendJsonAsync(
            blue,
            HttpMethod.Put,
            $"/api/mentor/students/{created.Student.EnrollmentId}",
            Student("223456789", "Jane Mary", "Doe"));
        Assert.Equal(HttpStatusCode.Conflict, blockedEdit.StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
            var attendance = await dbContext.AttendanceRecords.SingleAsync();
            Assert.Equal(_redGroupId, attendance.GroupId);
            Assert.Equal(AttendanceStatus.Present, attendance.Status);
        }

        _time.SetUtcNow(new DateTimeOffset(2027, 2, 2, 6, 0, 0, TimeSpan.Zero));
        using var blueTomorrow = await CreateMentorClientAsync("blue.mentor@orisync.test");
        var directory = await blueTomorrow.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/mentor/students?page=1&pageSize=10",
            JsonOptions);
        var tomorrow = Assert.Single(directory!.Items);
        Assert.Equal(_blueGroupId, tomorrow.GroupId);
        Assert.False(tomorrow.IsIncomingTransfer);
        Assert.Equal("Absent", tomorrow.AttendanceStatus);
    }

    [Fact]
    public async Task SameGroupReOnboardingIsImmediateAndEditingRotatesQrVersion()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        var createdResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane Mary", "Doe"));
        var created = (await createdResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        await SendJsonAsync(
            red,
            HttpMethod.Post,
            $"/api/mentor/students/{created.Student.EnrollmentId}/remove");

        var restoredResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students/onboard",
            new OnboardExistingStudentRequest("223456789"));
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        Assert.Equal("RestoredToday", restored.Outcome);
        Assert.Equal("Present", restored.Student.AttendanceStatus);

        var updatedResponse = await SendJsonAsync(
            red,
            HttpMethod.Put,
            $"/api/mentor/students/{created.Student.EnrollmentId}",
            Student("223456789", "Jane Marie", "Doe-Smith"));
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        Assert.Equal(2, updated.Student.QrVersion);
        Assert.Equal("JM", updated.Student.Initials);
        Assert.Equal("Doe-Smith", updated.Student.Surname);
    }

    [Fact]
    public async Task MentorCanDownloadDeterministicScannableQrForCurrentGroupOnly()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        var createdResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane Mary", "Doe"));
        var created = (await createdResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        var path = $"/api/mentor/students/{created.Student.EnrollmentId}/qr";

        var first = await red.GetAsync(path);
        var second = await red.GetAsync(path);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", first.Headers.CacheControl?.ToString());
        Assert.Equal("inline", first.Content.Headers.ContentDisposition?.DispositionType);
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        Assert.Equal(firstBytes, await second.Content.ReadAsByteArrayAsync());

        using var bitmap = SKBitmap.Decode(firstBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions { TryHarder = true }
        };
        var decoded = reader.Decode(new RGBLuminanceSource(
            bitmap.Bytes,
            bitmap.Width,
            bitmap.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32));
        Assert.NotNull(decoded);
        var uri = new Uri(decoded.Text);
        Assert.Empty(uri.Query);
        Assert.StartsWith("#payload=", uri.Fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("personal.test", decoded.Text, StringComparison.Ordinal);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<StudentQrCodeTokenService>();
            Assert.True(tokens.TryValidate(decoded.Text, out var payload));
            Assert.NotNull(payload);
            Assert.Equal("223456789", payload.StudentNumber);
            Assert.Equal("Jane Mary", payload.FullNames);
            Assert.Equal("Doe", payload.Surname);
            Assert.Equal("223456789@tut4life.ac.za", payload.StudentEmail);
            Assert.Equal(1, payload.Version);
            var tampered = decoded.Text.EndsWith('A')
                ? $"{decoded.Text[..^1]}B"
                : $"{decoded.Text[..^1]}A";
            Assert.False(tokens.TryValidate(tampered, out _));
            Assert.False(tokens.TryValidate(
                $"{decoded.Text}&payload=duplicate",
                out _));
        }

        var download = await red.GetAsync($"{path}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("223456789-qr.png", download.Content.Headers.ContentDisposition?.FileNameStar);

        using var blue = await CreateMentorClientAsync("blue.mentor@orisync.test");
        Assert.Equal(HttpStatusCode.NotFound, (await blue.GetAsync(path)).StatusCode);
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task StudentDetailEditCreatesReplacementQrButGroupMovementDoesNotChangeVersion()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        var createdResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane Mary", "Doe"));
        var created = (await createdResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        var qrPath = $"/api/mentor/students/{created.Student.EnrollmentId}/qr";
        var original = await red.GetByteArrayAsync(qrPath);

        var updatedResponse = await SendJsonAsync(
            red,
            HttpMethod.Put,
            $"/api/mentor/students/{created.Student.EnrollmentId}",
            Student("223456789", "Jane Marie", "Doe"));
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        Assert.Equal(2, updated.Student.QrVersion);
        Assert.NotEqual(original, await red.GetByteArrayAsync(qrPath));

        await SendJsonAsync(
            red,
            HttpMethod.Post,
            $"/api/mentor/students/{created.Student.EnrollmentId}/remove");
        using var blue = await CreateMentorClientAsync("blue.mentor@orisync.test");
        var movedResponse = await SendJsonAsync(
            blue,
            HttpMethod.Post,
            "/api/mentor/students/onboard",
            new OnboardExistingStudentRequest("223456789"));
        var moved = (await movedResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        Assert.Equal(2, moved.Student.QrVersion);
        Assert.Equal(HttpStatusCode.NotFound, (await blue.GetAsync(qrPath)).StatusCode);
    }

    [Fact]
    public async Task AdminDirectoryIsReadOnlyAndCanFilterUnassignedStudents()
    {
        using var red = await CreateMentorClientAsync("red.mentor@orisync.test");
        var createdResponse = await SendJsonAsync(
            red,
            HttpMethod.Post,
            "/api/mentor/students",
            Student("223456789", "Jane", "Doe"));
        var created = (await createdResponse.Content.ReadFromJsonAsync<StudentMutationResponse>(JsonOptions))!;
        await SendJsonAsync(
            red,
            HttpMethod.Post,
            $"/api/mentor/students/{created.Student.EnrollmentId}/remove");

        using var admin = await CreateAdminClientAsync();
        var directory = await admin.GetFromJsonAsync<StudentDirectoryResponse>(
            "/api/admin/students?unassigned=true&page=1&pageSize=10",
            JsonOptions);
        var student = Assert.Single(directory!.Items);
        Assert.Equal("UNASSIGNED", student.GroupName);

        var mutation = await SendJsonAsync(
            admin,
            HttpMethod.Put,
            $"/api/mentor/students/{created.Student.EnrollmentId}",
            Student("223456789", "Changed", "Name"));
        Assert.Equal(HttpStatusCode.Forbidden, mutation.StatusCode);
    }

    private HttpClient CreateClient() => _factory!.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private async Task<HttpClient> CreateMentorClientAsync(string email)
    {
        var client = CreateClient();
        (await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest(email, Password))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        (await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/auth/login",
            new LoginRequest("admin@orisync.test", Password))).EnsureSuccessStatusCode();
        return client;
    }

    private static SaveStudentRequest Student(
        string number,
        string fullNames,
        string surname) => new(
            number,
            fullNames,
            surname,
            $"{number}@tut4life.ac.za",
            $"{number}@personal.test",
            StudentAgeGroup.C2,
            Gender.Female,
            Ethnicity.African);

    private async Task<long> AddStudentWithoutAttendanceAsync(
        long groupId,
        string number,
        string fullNames,
        string surname,
        int qrVersion = 1)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        var group = await dbContext.Groups.SingleAsync(item => item.Id == groupId);
        var now = _time.GetUtcNow();
        var person = new Person
        {
            PersonType = PersonType.Student,
            InstitutionNumber = number,
            FirstName = fullNames,
            Surname = surname,
            Initials = string.Concat(fullNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => item[0])),
            AgeGroup = StudentAgeGroup.C2,
            Gender = Gender.Female,
            Ethnicity = Ethnicity.African,
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Student,
            Email = $"{number}@tut4life.ac.za",
            NormalizedEmail = $"{number}@tut4life.ac.za",
            CreatedAt = now
        });
        var enrollment = new StudentEnrollment
        {
            OrientationId = group.OrientationId,
            Student = person,
            CurrentGroupId = group.Id,
            QrVersion = qrVersion,
            EnrolledAt = now
        };
        dbContext.StudentEnrollments.Add(enrollment);
        await dbContext.SaveChangesAsync();
        return enrollment.Id;
    }

    private string CreateQrUrl(
        string studentNumber,
        string fullNames,
        string surname,
        int version)
    {
        var tokenService = _factory!.Services.GetRequiredService<StudentQrCodeTokenService>();
        return tokenService.Create(new StudentQrCodePayload(
            studentNumber,
            fullNames,
            surname,
            $"{studentNumber}@tut4life.ac.za",
            version)).Url;
    }

    private static async Task AssertAttendanceProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
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

    private static async Task<(long RedGroupId, long BlueGroupId)> SeedOrientationAndMentorsAsync(
        OriSyncDbContext dbContext)
    {
        var now = new DateTimeOffset(2027, 2, 1, 6, 0, 0, TimeSpan.Zero);
        var orientation = new Orientation
        {
            Year = 2027,
            Name = "First Year Orientation 2027",
            StartDate = new DateOnly(2027, 2, 1),
            EndDate = new DateOnly(2027, 2, 12),
            AttendanceOpensAt = new TimeOnly(7, 45),
            AttendanceClosesAt = new TimeOnly(15, 30),
            TimeZoneId = "Africa/Johannesburg",
            IsActive = true,
            RetentionDueAt = now.AddMonths(6),
            CreatedAt = now
        };
        var red = NewGroup(orientation, "RED", "#C62828", now);
        var blue = NewGroup(orientation, "BLUE", "#1565C0", now);
        dbContext.Groups.AddRange(red, blue);
        dbContext.Accounts.AddRange(
            NewAccount(AccountRole.Admin, "Admin", "User", "admin@orisync.test", null, now),
            NewAccount(AccountRole.Mentor, "Red", "Mentor", "red.mentor@orisync.test", red, now, "223456780"),
            NewAccount(AccountRole.Mentor, "Blue", "Mentor", "blue.mentor@orisync.test", blue, now, "223456781"));
        await dbContext.SaveChangesAsync();
        return (red.Id, blue.Id);
    }

    private static OrientationGroup NewGroup(
        Orientation orientation,
        string name,
        string color,
        DateTimeOffset now) => new()
        {
            Orientation = orientation,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            BadgeColor = color,
            IsActive = true,
            CreatedAt = now
        };

    private static Account NewAccount(
        AccountRole role,
        string firstName,
        string surname,
        string email,
        OrientationGroup? group,
        DateTimeOffset now,
        string? institutionNumber = null)
    {
        var person = new Person
        {
            PersonType = role == AccountRole.Admin ? PersonType.Admin : PersonType.Mentor,
            InstitutionNumber = institutionNumber,
            FirstName = firstName,
            Surname = surname,
            CreatedAt = now
        };
        person.Emails.Add(new PersonEmail
        {
            EmailType = EmailType.Login,
            Email = email,
            NormalizedEmail = email,
            CreatedAt = now
        });
        var account = new Account
        {
            Person = person,
            Role = role,
            CurrentGroup = group,
            IsActive = true,
            CreatedAt = now
        };
        account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, Password);
        return account;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
