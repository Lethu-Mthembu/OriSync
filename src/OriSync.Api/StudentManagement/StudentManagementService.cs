using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OriSync.Api.Attendance;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using OriSync.Api.Domain;

namespace OriSync.Api.StudentManagement;

public sealed partial class StudentManagementService(
    OriSyncDbContext dbContext,
    TimeProvider timeProvider,
    DailyAttendanceMaterializer attendanceMaterializer)
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];
    private const string StudentEmailDomain = "@tut4life.ac.za";

    public async Task<StudentDirectoryResponse> GetMentorDirectoryAsync(
        long accountId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, pageSize);
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        if (IsOrientationDay(context.Orientation, context.LocalDate))
        {
            await attendanceMaterializer.EnsureGroupAsync(
                context.Orientation.Id,
                context.Group.Id,
                context.LocalDate,
                cancellationToken);
        }

        var query = DirectoryQuery(context.Orientation.Id, context.LocalDate)
            .Where(item =>
                item.CurrentGroupId == context.Group.Id ||
                item.PendingGroupId == context.Group.Id);
        query = ApplySearch(query, search);
        return await PageAsync(query, page, pageSize, context.LocalDate, cancellationToken);
    }

    public async Task<StudentDirectoryResponse> GetAdminDirectoryAsync(
        string? search,
        long? groupId,
        bool unassigned,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, pageSize);
        var orientation = await dbContext.Orientations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IsActive, cancellationToken);
        if (orientation is null)
        {
            return new StudentDirectoryResponse([], page, pageSize, 0, 0);
        }

        var localDate = GetLocalDateTime(orientation).Date;
        await ReconcileTransfersAsync(orientation.Id, localDate, cancellationToken);
        if (IsOrientationDay(orientation, localDate))
        {
            await attendanceMaterializer.EnsureOrientationAsync(
                orientation.Id,
                localDate,
                cancellationToken);
        }
        var query = DirectoryQuery(orientation.Id, localDate);
        if (unassigned)
        {
            query = query.Where(item => item.CurrentGroupId == null && item.PendingGroupId == null);
        }
        else if (groupId is not null)
        {
            query = query.Where(item =>
                item.CurrentGroupId == groupId.Value ||
                item.PendingGroupId == groupId.Value);
        }

        query = ApplySearch(query, search);
        return await PageAsync(query, page, pageSize, localDate, cancellationToken);
    }

    public async Task<StudentLookupResponse> LookupAsync(
        long accountId,
        string studentNumber,
        CancellationToken cancellationToken)
    {
        var number = ValidateStudentNumber(studentNumber);
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);

        var person = await dbContext.People
            .AsNoTracking()
            .Where(item => item.InstitutionNumber == number)
            .Include(item => item.Emails)
            .SingleOrDefaultAsync(cancellationToken);
        if (person is null)
        {
            return new StudentLookupResponse("New", number, null, null, null, null, null);
        }

        if (person.PersonType != PersonType.Student)
        {
            return NumberUnavailable(number);
        }

        var enrollment = await dbContext.StudentEnrollments
            .AsNoTracking()
            .Include(item => item.CurrentGroup)
            .Include(item => item.PendingGroup)
            .SingleOrDefaultAsync(
                item => item.OrientationId == context.Orientation.Id && item.StudentId == person.Id,
                cancellationToken);
        if (enrollment is null)
        {
            // Student data must be purged before the next orientation. Reusing an
            // orphaned identity would silently violate that retention rule.
            return NumberUnavailable(number);
        }

        var assignedGroup = enrollment.PendingGroup ?? enrollment.CurrentGroup;
        if (assignedGroup is not null)
        {
            return new StudentLookupResponse(
                "Assigned",
                number,
                person.FirstName,
                person.Surname,
                StudentEmail(person),
                assignedGroup.Name,
                $"This student already belongs to {assignedGroup.Name} Group.");
        }

        return new StudentLookupResponse(
            "Unassigned",
            number,
            person.FirstName,
            person.Surname,
            StudentEmail(person),
            "UNASSIGNED",
            $"Add {person.FirstName} {person.Surname} ({number}) to {context.Group.Name} Group?");
    }

    public async Task<StudentDirectoryItemResponse> GetProfileAsync(
        long accountId,
        long enrollmentId,
        CancellationToken cancellationToken)
    {
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        var enrollment = await DirectoryQuery(context.Orientation.Id, context.LocalDate)
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId &&
                    (item.CurrentGroupId == context.Group.Id ||
                        item.PendingGroupId == context.Group.Id),
                cancellationToken)
            ?? throw NotFound();
        return Map(enrollment, context.LocalDate);
    }

    public async Task<StudentMutationResponse> CreateAsync(
        long accountId,
        SaveStudentRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        EnsureRegistrationOpen(context);
        var values = Validate(request);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existingNumber = await dbContext.People
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.InstitutionNumber == values.StudentNumber,
                cancellationToken);
        if (existingNumber is not null)
        {
            if (existingNumber.PersonType == PersonType.Student)
            {
                throw Conflict("This student already exists. Use exact student-number onboarding.");
            }

            throw Conflict("This student number is already in use.");
        }

        await EnsureEmailsAvailableAsync(values, null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var person = new Person
        {
            PersonType = PersonType.Student,
            InstitutionNumber = values.StudentNumber,
            FirstName = values.FullNames,
            Surname = values.Surname,
            Initials = GenerateInitials(values.FullNames),
            AgeGroup = values.AgeGroup,
            Gender = values.Gender,
            Ethnicity = values.Ethnicity,
            CreatedAt = now
        };
        AddEmails(person, values, now);
        var enrollment = new StudentEnrollment
        {
            OrientationId = context.Orientation.Id,
            Student = person,
            CurrentGroupId = context.Group.Id,
            QrVersion = 1,
            EnrolledAt = now
        };
        enrollment.AttendanceRecords.Add(new AttendanceRecord
        {
            OrientationId = context.Orientation.Id,
            GroupId = context.Group.Id,
            AttendanceDate = context.LocalDate,
            Status = AttendanceStatus.Present,
            CaptureMethod = AttendanceCaptureMethod.Registration,
            MarkedByAccountId = accountId
        });
        dbContext.StudentEnrollments.Add(enrollment);
        await SaveChangesAsync("The student could not be registered.", cancellationToken);
        AddAudit(accountId, "StudentCreated", enrollment.Id, now, new { values.StudentNumber, context.Group.Id });
        await SaveChangesAsync("The student registration audit could not be recorded.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        enrollment.CurrentGroup = context.Group;
        return new StudentMutationResponse(
            Map(enrollment, context.LocalDate),
            "RegisteredPresent");
    }

    public async Task<StudentMutationResponse> OnboardExistingAsync(
        long accountId,
        OnboardExistingStudentRequest request,
        CancellationToken cancellationToken)
    {
        var number = ValidateStudentNumber(request.StudentNumber);
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        EnsureRegistrationOpen(context);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .Include(item => item.CurrentGroup)
            .Include(item => item.PendingGroup)
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == context.LocalDate))
            .SingleOrDefaultAsync(
                item => item.OrientationId == context.Orientation.Id &&
                    item.Student.InstitutionNumber == number,
                cancellationToken)
            ?? throw new StudentManagementException(
                StatusCodes.Status404NotFound,
                "Student not found.",
                "No current-orientation student matches this number. Register a new student instead.");

        var assignedGroup = enrollment.PendingGroup ?? enrollment.CurrentGroup;
        if (assignedGroup is not null)
        {
            throw Conflict($"This student already belongs to {assignedGroup.Name} Group.");
        }

        var now = timeProvider.GetUtcNow();
        var attendance = enrollment.AttendanceRecords.SingleOrDefault();
        string outcome;
        if (attendance is null)
        {
            enrollment.CurrentGroupId = context.Group.Id;
            enrollment.CurrentGroup = context.Group;
            enrollment.RemovedAt = null;
            attendance = new AttendanceRecord
            {
                OrientationId = context.Orientation.Id,
                GroupId = context.Group.Id,
                AttendanceDate = context.LocalDate,
                Status = AttendanceStatus.Present,
                CaptureMethod = AttendanceCaptureMethod.Registration,
                MarkedByAccountId = accountId
            };
            enrollment.AttendanceRecords.Add(attendance);
            AddAudit(accountId, "StudentAssigned", enrollment.Id, now, new { context.Group.Id });
            outcome = "AssignedPresent";
        }
        else if (attendance.GroupId == context.Group.Id)
        {
            // Restoring the student to the group that owns today's attendance
            // takes effect immediately and preserves that recorded status.
            enrollment.CurrentGroupId = context.Group.Id;
            enrollment.CurrentGroup = context.Group;
            enrollment.RemovedAt = null;
            AddAudit(accountId, "StudentRestored", enrollment.Id, now, new { context.Group.Id });
            outcome = "RestoredToday";
        }
        else
        {
            // The destination may see the student today, but today's attendance
            // remains a snapshot of the source group. The assignment itself is
            // activated on the next calendar day.
            enrollment.PendingGroupId = context.Group.Id;
            enrollment.PendingGroup = context.Group;
            enrollment.PendingGroupEffectiveDate = context.LocalDate.AddDays(1);
            outcome = "MovesTomorrow";
        }

        await SaveChangesAsync("The student could not be onboarded.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StudentMutationResponse(Map(enrollment, context.LocalDate), outcome);
    }

    public async Task<StudentMutationResponse> UpdateAsync(
        long accountId,
        long enrollmentId,
        SaveStudentRequest request,
        CancellationToken cancellationToken)
    {
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        var values = Validate(request);
        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .Include(item => item.CurrentGroup)
            .Include(item => item.PendingGroup)
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == context.LocalDate))
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId && item.OrientationId == context.Orientation.Id,
                cancellationToken)
            ?? throw NotFound();

        if (enrollment.PendingGroupId == context.Group.Id && enrollment.CurrentGroupId != context.Group.Id)
        {
            throw Conflict("This student becomes editable by your group tomorrow.");
        }

        if (enrollment.CurrentGroupId != context.Group.Id)
        {
            throw NotFound();
        }

        await EnsureNumberAvailableAsync(values.StudentNumber, enrollment.StudentId, cancellationToken);
        await EnsureEmailsAvailableAsync(values, enrollment.StudentId, cancellationToken);
        var changed = ChangedFields(enrollment.Student, values);
        if (changed.Count != 0)
        {
            Apply(enrollment.Student, values);
            enrollment.QrVersion++;
            var now = timeProvider.GetUtcNow();
            ReplaceEmails(enrollment.Student, values, now);
            AddAudit(accountId, "StudentUpdated", enrollment.Id, now, new { fields = changed });
            await SaveChangesAsync("The student could not be updated.", cancellationToken);
        }

        return new StudentMutationResponse(Map(enrollment, context.LocalDate), "Updated");
    }

    public async Task RemoveAsync(
        long accountId,
        long enrollmentId,
        CancellationToken cancellationToken)
    {
        var context = await GetMentorContextAsync(accountId, cancellationToken);
        await ReconcileTransfersAsync(context.Orientation.Id, context.LocalDate, cancellationToken);
        var enrollment = await dbContext.StudentEnrollments
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == context.LocalDate))
            .SingleOrDefaultAsync(
                item => item.Id == enrollmentId && item.OrientationId == context.Orientation.Id,
                cancellationToken)
            ?? throw NotFound();

        if (enrollment.PendingGroupId == context.Group.Id && enrollment.CurrentGroupId != context.Group.Id)
        {
            throw Conflict("A next-day group assignment cannot be cancelled.");
        }

        if (enrollment.CurrentGroupId != context.Group.Id)
        {
            throw NotFound();
        }

        var now = timeProvider.GetUtcNow();
        if (IsOrientationDay(context.Orientation, context.LocalDate) &&
            enrollment.AttendanceRecords.Count == 0)
        {
            // Materialize today's default Absent state before releasing the
            // student so later onboarding cannot rewrite the source group.
            enrollment.AttendanceRecords.Add(new AttendanceRecord
            {
                OrientationId = context.Orientation.Id,
                GroupId = context.Group.Id,
                AttendanceDate = context.LocalDate,
                Status = AttendanceStatus.Absent,
                CaptureMethod = AttendanceCaptureMethod.Automatic
            });
        }

        enrollment.CurrentGroupId = null;
        enrollment.CurrentGroup = null;
        enrollment.RemovedAt = now;
        AddAudit(accountId, "StudentRemoved", enrollment.Id, now, new { context.Group.Id });
        await SaveChangesAsync("The student could not be removed from the group.", cancellationToken);
    }

    private IQueryable<StudentEnrollment> DirectoryQuery(long orientationId, DateOnly localDate) =>
        dbContext.StudentEnrollments
            .AsNoTracking()
            .Where(item => item.OrientationId == orientationId)
            .Include(item => item.Student)
                .ThenInclude(student => student.Emails)
            .Include(item => item.CurrentGroup)
            .Include(item => item.PendingGroup)
            .Include(item => item.AttendanceRecords.Where(record =>
                record.AttendanceDate == localDate));

    private static IQueryable<StudentEnrollment> ApplySearch(
        IQueryable<StudentEnrollment> query,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var pattern = $"%{search.Trim()}%";
        return query.Where(item =>
            EF.Functions.ILike(item.Student.InstitutionNumber!, pattern) ||
            EF.Functions.ILike(item.Student.FirstName, pattern) ||
            EF.Functions.ILike(item.Student.Surname, pattern));
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || !AllowedPageSizes.Contains(pageSize))
        {
            throw Invalid("Page must be positive and page size must be 10, 20, 50 or 100.");
        }
    }

    private static async Task<StudentDirectoryResponse> PageAsync(
        IQueryable<StudentEnrollment> query,
        int page,
        int pageSize,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.Student.InstitutionNumber)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        return new StudentDirectoryResponse(
            items.Select(item => Map(item, localDate)).ToArray(),
            page,
            pageSize,
            totalItems,
            totalPages);
    }

    private async Task<MentorContext> GetMentorContextAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .Include(item => item.CurrentGroup)
                .ThenInclude(group => group!.Orientation)
            .SingleOrDefaultAsync(
                item => item.Id == accountId && item.Role == AccountRole.Mentor && item.IsActive,
                cancellationToken)
            ?? throw new StudentManagementException(
                StatusCodes.Status401Unauthorized,
                "Authentication required.",
                "The mentor account is unavailable.");
        var group = account.CurrentGroup;
        if (group is null || !group.IsActive || !group.Orientation.IsActive)
        {
            throw Conflict("Your account is not assigned to an active orientation group.");
        }

        var local = GetLocalDateTime(group.Orientation);
        return new MentorContext(account, group, group.Orientation, local.Date, local.Time);
    }

    private static void EnsureRegistrationOpen(MentorContext context)
    {
        if (!IsOrientationDay(context.Orientation, context.LocalDate) ||
            context.LocalTime < context.Orientation.AttendanceOpensAt ||
            context.LocalTime > context.Orientation.AttendanceClosesAt)
        {
            throw Conflict(
                $"Student registration is available only on orientation weekdays from " +
                $"{context.Orientation.AttendanceOpensAt:HH:mm} to " +
                $"{context.Orientation.AttendanceClosesAt:HH:mm}.");
        }
    }

    private static bool IsOrientationDay(Orientation orientation, DateOnly date) =>
        date >= orientation.StartDate &&
        date <= orientation.EndDate &&
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private (DateOnly Date, TimeOnly Time) GetLocalDateTime(Orientation orientation)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(orientation.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"Configured orientation timezone '{orientation.TimeZoneId}' is unavailable.");
        }

        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    private async Task ReconcileTransfersAsync(
        long orientationId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        await dbContext.StudentEnrollments
            .Where(item => item.OrientationId == orientationId &&
                item.PendingGroupId != null &&
                item.PendingGroupEffectiveDate <= localDate)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentGroupId, item => item.PendingGroupId)
                    .SetProperty(item => item.PendingGroupId, (long?)null)
                    .SetProperty(item => item.PendingGroupEffectiveDate, (DateOnly?)null)
                    .SetProperty(item => item.RemovedAt, (DateTimeOffset?)null),
                cancellationToken);
    }

    private static StudentDirectoryItemResponse Map(
        StudentEnrollment enrollment,
        DateOnly localDate)
    {
        var incoming = enrollment.PendingGroupId is not null &&
            enrollment.PendingGroupEffectiveDate > localDate;
        var group = incoming ? enrollment.PendingGroup : enrollment.CurrentGroup;
        var attendance = enrollment.AttendanceRecords.SingleOrDefault(item =>
            item.AttendanceDate == localDate);
        return new StudentDirectoryItemResponse(
            enrollment.Id,
            enrollment.Student.InstitutionNumber!,
            enrollment.Student.FirstName,
            enrollment.Student.Surname,
            enrollment.Student.Initials!,
            StudentEmail(enrollment.Student),
            PersonalEmail(enrollment.Student),
            enrollment.Student.AgeGroup!.Value,
            enrollment.Student.Gender!.Value,
            enrollment.Student.Ethnicity!.Value,
            group?.Id,
            group?.Name ?? "UNASSIGNED",
            group?.BadgeColor ?? "#64748B",
            incoming ? "—" : attendance?.Status.ToString() ?? "Absent",
            incoming,
            incoming ? enrollment.PendingGroupEffectiveDate : null,
            enrollment.QrVersion);
    }

    private static ValidatedStudent Validate(SaveStudentRequest request)
    {
        var number = ValidateStudentNumber(request.StudentNumber);
        var fullNames = ValidateName(request.FullNames, "Full names");
        var surname = ValidateName(request.Surname, "Surname");
        var submittedStudentEmail = ValidateEmail(request.StudentEmail, "Student email");
        var studentEmail = $"{number}{StudentEmailDomain}";
        if (!string.Equals(
                submittedStudentEmail,
                studentEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"Student email must be {studentEmail}.");
        }
        var personalEmail = string.IsNullOrWhiteSpace(request.PersonalEmail)
            ? null
            : ValidateEmail(request.PersonalEmail, "Personal email");
        if (personalEmail is not null &&
            string.Equals(studentEmail, personalEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Student email and personal email must be different.");
        }

        if (!Enum.IsDefined(request.AgeGroup) ||
            !Enum.IsDefined(request.Gender) ||
            !Enum.IsDefined(request.Ethnicity))
        {
            throw Invalid("Age group, gender and ethnicity are required.");
        }

        return new ValidatedStudent(
            number,
            fullNames,
            surname,
            studentEmail,
            personalEmail,
            request.AgeGroup,
            request.Gender,
            request.Ethnicity);
    }

    private static string ValidateStudentNumber(string? value)
    {
        var number = value?.Trim() ?? string.Empty;
        if (!StudentNumberRegex().IsMatch(number))
        {
            throw Invalid("Student number must contain nine digits and begin with 2.");
        }

        return number;
    }

    private static string ValidateName(string? value, string field)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (normalized.Length is < 1 or > 100 || !PersonNameRegex().IsMatch(normalized))
        {
            throw Invalid(
                $"{field} must use only letters, spaces, apostrophes and hyphens and contain at most 100 characters.");
        }

        return normalized;
    }

    private static string ValidateEmail(string? value, string field)
    {
        var email = value?.Trim() ?? string.Empty;
        if (email.Length is < 3 or > 254)
        {
            throw Invalid($"{field} must be a valid email address.");
        }

        try
        {
            var parsed = new MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
        }
        catch (FormatException)
        {
            throw Invalid($"{field} must be a valid email address.");
        }

        return email;
    }

    private async Task EnsureNumberAvailableAsync(
        string number,
        long studentId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.People.AnyAsync(
                item => item.InstitutionNumber == number && item.Id != studentId,
                cancellationToken))
        {
            throw Conflict("This student number is already in use.");
        }
    }

    private async Task EnsureEmailsAvailableAsync(
        ValidatedStudent values,
        long? studentId,
        CancellationToken cancellationToken)
    {
        var normalized = new[] { values.StudentEmail, values.PersonalEmail }
            .Where(item => item is not null)
            .Select(item => item!.ToLowerInvariant())
            .ToArray();
        if (await dbContext.PersonEmails.AnyAsync(
                item => normalized.Contains(item.NormalizedEmail) &&
                    (studentId == null || item.PersonId != studentId.Value),
                cancellationToken))
        {
            throw Conflict("One of these email addresses is already in use.");
        }
    }

    private static void AddEmails(Person person, ValidatedStudent values, DateTimeOffset now)
    {
        person.Emails.Add(NewEmail(EmailType.Student, values.StudentEmail, now));
        if (values.PersonalEmail is not null)
        {
            person.Emails.Add(NewEmail(EmailType.Personal, values.PersonalEmail, now));
        }
    }

    private static void ReplaceEmails(Person person, ValidatedStudent values, DateTimeOffset now)
    {
        var studentEmail = person.Emails.Single(item => item.EmailType == EmailType.Student);
        studentEmail.Email = values.StudentEmail;
        studentEmail.NormalizedEmail = values.StudentEmail.ToLowerInvariant();
        var personalEmail = person.Emails.SingleOrDefault(item => item.EmailType == EmailType.Personal);
        if (values.PersonalEmail is null)
        {
            if (personalEmail is not null)
            {
                person.Emails.Remove(personalEmail);
            }
        }
        else if (personalEmail is null)
        {
            person.Emails.Add(NewEmail(EmailType.Personal, values.PersonalEmail, now));
        }
        else
        {
            personalEmail.Email = values.PersonalEmail;
            personalEmail.NormalizedEmail = values.PersonalEmail.ToLowerInvariant();
        }
    }

    private static PersonEmail NewEmail(EmailType type, string email, DateTimeOffset now) => new()
    {
        EmailType = type,
        Email = email,
        NormalizedEmail = email.ToLowerInvariant(),
        CreatedAt = now
    };

    private static List<string> ChangedFields(Person person, ValidatedStudent values)
    {
        var fields = new List<string>();
        if (person.InstitutionNumber != values.StudentNumber) fields.Add("studentNumber");
        if (person.FirstName != values.FullNames) fields.Add("fullNames");
        if (person.Surname != values.Surname) fields.Add("surname");
        if (person.AgeGroup != values.AgeGroup) fields.Add("ageGroup");
        if (person.Gender != values.Gender) fields.Add("gender");
        if (person.Ethnicity != values.Ethnicity) fields.Add("ethnicity");
        if (!string.Equals(StudentEmail(person), values.StudentEmail, StringComparison.Ordinal)) fields.Add("studentEmail");
        if (!string.Equals(PersonalEmail(person), values.PersonalEmail, StringComparison.Ordinal)) fields.Add("personalEmail");
        return fields;
    }

    private static void Apply(Person person, ValidatedStudent values)
    {
        person.InstitutionNumber = values.StudentNumber;
        person.FirstName = values.FullNames;
        person.Surname = values.Surname;
        person.Initials = GenerateInitials(values.FullNames);
        person.AgeGroup = values.AgeGroup;
        person.Gender = values.Gender;
        person.Ethnicity = values.Ethnicity;
    }

    private static string GenerateInitials(string fullNames) => string.Concat(
        fullNames.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => StringInfo.GetNextTextElement(item).ToUpperInvariant()));

    private static string StudentEmail(Person person) =>
        person.Emails.Single(item => item.EmailType == EmailType.Student).Email;

    private static string? PersonalEmail(Person person) =>
        person.Emails.SingleOrDefault(item => item.EmailType == EmailType.Personal)?.Email;

    private void AddAudit(
        long actorAccountId,
        string eventType,
        long? enrollmentId,
        DateTimeOffset now,
        object details)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            AccountId = actorAccountId,
            EventType = eventType,
            EntityType = "StudentEnrollment",
            EntityId = enrollmentId,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAt = now,
            ExpiresAt = now + AuthenticationConstants.AuditLifetime
        });
    }

    private async Task SaveChangesAsync(string detail, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw Conflict(detail);
        }
    }

    private static StudentLookupResponse NumberUnavailable(string number) => new(
        "Unavailable",
        number,
        null,
        null,
        null,
        null,
        "This student number is already in use.");

    private static StudentManagementException Invalid(string detail) => new(
        StatusCodes.Status400BadRequest,
        "Invalid student details.",
        detail);

    private static StudentManagementException Conflict(string detail) => new(
        StatusCodes.Status409Conflict,
        "Student operation rejected.",
        detail);

    private static StudentManagementException NotFound() => new(
        StatusCodes.Status404NotFound,
        "Student not found.",
        "The student is not available to this mentor group.");

    [GeneratedRegex("^2[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex StudentNumberRegex();

    [GeneratedRegex(@"^[\p{L}\p{M}]+(?:[ '\u2019\-\u2010\u2011][\p{L}\p{M}]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PersonNameRegex();

    private sealed record MentorContext(
        Account Account,
        OrientationGroup Group,
        Orientation Orientation,
        DateOnly LocalDate,
        TimeOnly LocalTime);

    private sealed record ValidatedStudent(
        string StudentNumber,
        string FullNames,
        string Surname,
        string StudentEmail,
        string? PersonalEmail,
        StudentAgeGroup AgeGroup,
        Gender Gender,
        Ethnicity Ethnicity);
}
